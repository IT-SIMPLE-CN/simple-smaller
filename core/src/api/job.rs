//! 任务：后台线程执行，逐个回调进度与结果，可取消、可整批撤销。

use std::collections::HashSet;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use crate::jobs::{self, ItemResult, ItemStatus, Plan};
use crate::EngineError;
use super::types::*;

// ---------- 任务 ----------

#[derive(uniffi::Object)]
pub struct Job {
    pub(super) cancel: AtomicBool,
    pub(super) finished: AtomicBool,
    pub(super) results: Mutex<Vec<ItemResult>>,
    pub(super) created_roots: Mutex<Vec<PathBuf>>,
    /// 这一批要处理的文件（按序号），以及已经报过结果的序号：出意外时据此把剩下的标成失败。
    pub(super) planned: Mutex<Vec<PathBuf>>,
    pub(super) reported: Mutex<HashSet<usize>>,
}

#[uniffi::export]
impl Job {
    pub fn cancel(&self) {
        self.cancel.store(true, Ordering::Relaxed);
    }

    pub fn is_finished(&self) -> bool {
        self.finished.load(Ordering::Relaxed)
    }

    /// 把这一批的产出移到废纸篓。
    pub fn undo(&self) -> Result<(), EngineError> {
        let results = self.results.lock().unwrap_or_else(|e| e.into_inner()).clone();
        let roots = self.created_roots.lock().unwrap_or_else(|e| e.into_inner()).clone();
        jobs::undo(&results, &roots).map_err(|message| EngineError::Failed { message })
    }
}

impl Job {
    pub(super) fn new() -> Arc<Self> {
        Arc::new(Job {
            cancel: AtomicBool::new(false),
            finished: AtomicBool::new(false),
            results: Mutex::new(Vec::new()),
            created_roots: Mutex::new(Vec::new()),
            planned: Mutex::new(Vec::new()),
            reported: Mutex::new(HashSet::new()),
        })
    }

    /// 记下展开后的文件与会新建的输出根目录（撤销、出意外时要用）。
    pub(super) fn set_plan(&self, plan: &Plan) {
        *self.created_roots.lock().unwrap_or_else(|e| e.into_inner()) = plan.created_roots.clone();
        *self.planned.lock().unwrap_or_else(|e| e.into_inner()) = plan.items.iter().map(|i| i.source.clone()).collect();
    }

    pub(super) fn record(&self, listener: &Arc<dyn JobListener>, r: ItemResult) {
        self.reported.lock().unwrap_or_else(|e| e.into_inner()).insert(r.index);
        listener.on_item(JobItemResult::from(&r));
        self.results.lock().unwrap_or_else(|e| e.into_inner()).push(r);
    }

    /// 只报一次「完成」。
    pub(super) fn finish(&self, listener: &Arc<dyn JobListener>) {
        if !self.finished.swap(true, Ordering::AcqRel) {
            listener.on_finished();
        }
    }

    /// 还没报过结果的文件一律标成失败。
    fn fail_rest(&self, listener: &Arc<dyn JobListener>, message: &str) {
        let planned = self.planned.lock().unwrap_or_else(|e| e.into_inner()).clone();
        for (index, source) in planned.into_iter().enumerate() {
            if self.reported.lock().unwrap_or_else(|e| e.into_inner()).contains(&index) {
                continue;
            }
            let before_bytes = std::fs::metadata(&source).map(|m| m.len()).unwrap_or(0);
            self.record(listener, ItemResult { index, source, before_bytes, outputs: Vec::new(), status: ItemStatus::Failed(message.to_string()) });
        }
    }
}

/// 线程结束时（包括 panic 展开时）一定报「完成」，界面不会一直停在处理中。
struct FinishGuard {
    job: Arc<Job>,
    listener: Arc<dyn JobListener>,
}

impl Drop for FinishGuard {
    fn drop(&mut self) {
        self.job.finish(&self.listener);
    }
}

/// 在后台线程跑一批。里面出了意外（panic）也不会让整批卡住：没报过结果的文件标成失败，最后一定报「完成」。
pub(super) fn spawn_job<F>(job: &Arc<Job>, listener: Arc<dyn JobListener>, body: F)
where
    F: FnOnce(&Arc<Job>, &Arc<dyn JobListener>) + Send + 'static,
{
    let job = job.clone();
    std::thread::spawn(move || {
        let _guard = FinishGuard { job: job.clone(), listener: listener.clone() };
        if let Err(panic) = catch_unwind(AssertUnwindSafe(|| body(&job, &listener))) {
            let detail = panic
                .downcast_ref::<&str>()
                .map(|s| s.to_string())
                .or_else(|| panic.downcast_ref::<String>().cloned())
                .unwrap_or_default();
            eprintln!("smaller-core: 任务线程出错：{detail}");
            let _ = catch_unwind(AssertUnwindSafe(|| job.fail_rest(&listener, "处理时出错，请重试")));
        }
    });
}
