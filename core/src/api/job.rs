//! 任务：后台线程执行，逐个回调进度与结果，可取消、可整批撤销。

use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use crate::jobs::{self, ItemResult};
use crate::EngineError;
use super::types::*;

// ---------- 任务 ----------

#[derive(uniffi::Object)]
pub struct Job {
    pub(super) cancel: AtomicBool,
    pub(super) finished: AtomicBool,
    pub(super) results: Mutex<Vec<ItemResult>>,
    pub(super) created_roots: Mutex<Vec<PathBuf>>,
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
        Arc::new(Job { cancel: AtomicBool::new(false), finished: AtomicBool::new(false), results: Mutex::new(Vec::new()), created_roots: Mutex::new(Vec::new()) })
    }

    pub(super) fn record(&self, listener: &Arc<dyn JobListener>, r: ItemResult) {
        listener.on_item(JobItemResult::from(&r));
        self.results.lock().unwrap_or_else(|e| e.into_inner()).push(r);
    }

    pub(super) fn finish(&self, listener: &Arc<dyn JobListener>) {
        self.finished.store(true, Ordering::Relaxed);
        listener.on_finished();
    }
}
