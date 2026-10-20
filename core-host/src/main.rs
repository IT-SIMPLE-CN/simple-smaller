//! 引擎宿主 `smaller-core`：把压缩引擎作为独立进程运行，Windows 应用经它的标准输入输出调用。
//!
//! 协议：每行一个 JSON（UTF-8，`\n` 结尾）。
//! - 请求（应用 → 引擎）：`{"id":1,"method":"plan","params":{…}}`
//! - 响应（引擎 → 应用）：`{"id":1,"result":…}` 或 `{"id":1,"error":{"kind":"io|unsupported|failed","message":"…"}}`
//! - 事件（引擎 → 应用，没有 id）：
//!   `{"event":"ready","protocol":1}` 启动好了；
//!   `{"event":"progress","job":7,"index":0,"fraction":0.5}`、`{"event":"item","job":7,"result":{…}}`、`{"event":"finished","job":7}` 任务进度、单个文件结果、任务结束；
//!   `{"event":"log","level":"error","message":"…"}` 日志。
//!
//! 每个请求在自己的线程里处理，互不阻塞；任务（run_*）由应用给出编号，宿主立即回响应（带展开后的文件与新建的输出根目录），
//! 之后在后台跑，事件按任务编号回报。撤销不依赖宿主里的状态（应用带上输出与根目录），宿主重启后照样能撤。
//! 标准输入关闭（应用退出）时宿主随即退出。

mod dto;

use std::collections::HashMap;
use std::io::{BufRead, Write};
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::path::PathBuf;
use std::sync::{Arc, Mutex, OnceLock};

use serde::Deserialize;
use serde_json::{Value, json};
use smaller_core::EngineError;
use smaller_core::api::{Engine, Job, JobItemResult, JobListener};
use smaller_core::jobs::{self, ItemResult, ItemStatus, OutputFile};

/// 协议版本：应用与宿主不配套时（升级到一半）应用据此报错。
const PROTOCOL: u32 = 1;

// ---------- 输出 ----------

/// 所有响应与事件都经这一把锁写出，一行一条，不会交错。
fn send(message: &Value) {
    static OUT: OnceLock<Mutex<std::io::Stdout>> = OnceLock::new();
    let mut line = message.to_string();
    line.push('\n');
    let out = OUT.get_or_init(|| Mutex::new(std::io::stdout()));
    let mut out = out.lock().unwrap_or_else(|e| e.into_inner());
    // 应用那头已经关了管道：没人收了，直接退出。
    if out.write_all(line.as_bytes()).and_then(|_| out.flush()).is_err() {
        std::process::exit(0);
    }
}

fn log(level: &str, message: impl Into<String>) {
    send(&json!({ "event": "log", "level": level, "message": message.into() }));
}

// ---------- 任务 ----------

/// 跑着的任务，按应用给的编号存；结束时移除。只用来取消。
type Jobs = Arc<Mutex<HashMap<u64, Arc<Job>>>>;

struct Listener {
    job: u64,
    jobs: Jobs,
}

impl JobListener for Listener {
    fn on_progress(&self, index: u32, fraction: f64) {
        send(&json!({ "event": "progress", "job": self.job, "index": index, "fraction": fraction }));
    }

    fn on_item(&self, result: JobItemResult) {
        send(&json!({ "event": "item", "job": self.job, "result": dto::JobItemResult::from(result) }));
    }

    fn on_finished(&self) {
        self.jobs.lock().unwrap_or_else(|e| e.into_inner()).remove(&self.job);
        send(&json!({ "event": "finished", "job": self.job }));
    }
}

// ---------- 请求 ----------

#[derive(Deserialize)]
struct Request {
    id: u64,
    method: String,
    #[serde(default)]
    params: Value,
}

/// 请求失败：种类与引擎的 EngineError 对应，应用照原样还原成异常。
struct Failure {
    kind: &'static str,
    message: String,
}

impl From<EngineError> for Failure {
    fn from(e: EngineError) -> Self {
        match e {
            EngineError::Io { message } => Failure { kind: "io", message },
            EngineError::Unsupported { message } => Failure { kind: "unsupported", message },
            EngineError::Failed { message } => Failure { kind: "failed", message },
        }
    }
}

impl From<serde_json::Error> for Failure {
    fn from(e: serde_json::Error) -> Self {
        Failure { kind: "failed", message: format!("请求格式不对：{e}") }
    }
}

fn params<T: for<'de> Deserialize<'de>>(p: &Value) -> Result<T, Failure> {
    Ok(T::deserialize(p)?)
}

#[derive(Deserialize)]
struct InputsKind {
    inputs: Vec<String>,
    kind: dto::Kind,
}

#[derive(Deserialize)]
struct InputsKindPlace {
    inputs: Vec<String>,
    kind: dto::Kind,
    place: dto::Place,
}

#[derive(Deserialize)]
struct PathOnly {
    path: String,
}

#[derive(Deserialize)]
struct Run<S> {
    job: u64,
    inputs: Vec<String>,
    place: dto::Place,
    naming: dto::Naming,
    settings: S,
}

#[derive(Deserialize)]
struct UndoParams {
    outputs: Vec<String>,
    created_roots: Vec<String>,
}

struct Host {
    engine: Arc<Engine>,
    jobs: Jobs,
}

impl Host {
    fn handle(&self, method: &str, p: &Value) -> Result<Value, Failure> {
        let e = &self.engine;
        Ok(match method {
            "plan" => {
                let a: InputsKindPlace = params(p)?;
                let s = e.plan(a.inputs, a.kind.into(), a.place.into());
                json!(dto::PlanSummary { files: s.files, skipped: s.skipped })
            }
            "plan_targets" => {
                let a: InputsKindPlace = params(p)?;
                let t = e.plan_targets(a.inputs, a.kind.into(), a.place.into());
                json!(dto::PlanTargets {
                    items: t.items.into_iter().map(|i| dto::PlanTarget { source: i.source, out_dir: i.out_dir }).collect(),
                    created_roots: t.created_roots,
                })
            }
            "list_files" => {
                let a: InputsKind = params(p)?;
                json!(e.list_files(a.inputs, a.kind.into()))
            }
            "preview_name" => {
                #[derive(Deserialize)]
                struct A {
                    naming: dto::Naming,
                    source_name: String,
                    width: u32,
                    height: u32,
                }
                let a: A = params(p)?;
                json!(e.preview_name(a.naming.into(), a.source_name, a.width, a.height))
            }
            "video_meta" => {
                let a: PathOnly = params(p)?;
                let m = e.video_meta(a.path)?;
                json!(dto::VideoMeta { duration: m.duration, width: m.width, height: m.height, fps: m.fps, codec: m.codec, bitrate: m.bitrate, has_audio: m.has_audio, hdr: m.hdr, bytes: m.bytes })
            }
            "pdf_meta" => {
                let a: PathOnly = params(p)?;
                let m = e.pdf_meta(a.path)?;
                json!(dto::PdfMeta { pages: m.pages, needs_password: m.needs_password, bytes: m.bytes })
            }
            "unlock_pdf" => {
                #[derive(Deserialize)]
                struct A {
                    path: String,
                    password: String,
                    output: String,
                }
                let a: A = params(p)?;
                e.unlock_pdf(a.path, a.password, a.output)?;
                Value::Null
            }
            "trash" => {
                #[derive(Deserialize)]
                struct A {
                    paths: Vec<String>,
                }
                let a: A = params(p)?;
                e.trash(a.paths)?;
                Value::Null
            }
            "compress_for_clipboard" => {
                #[derive(Deserialize)]
                struct A {
                    path: String,
                    max_long_edge: u32,
                }
                let a: A = params(p)?;
                match e.compress_for_clipboard(a.path, a.max_long_edge)? {
                    Some(c) => json!(dto::ClipboardImage { png: c.png, width: c.width, height: c.height, before_bytes: c.before_bytes, bytes: dto::base64(&c.bytes) }),
                    None => Value::Null,
                }
            }
            "run_images" => {
                let a: Run<dto::ImageSettings> = params(p)?;
                let planned = self.planned(&a.inputs, smaller_core::api::Kind::Image, &a.place);
                self.start(a.job, planned, |l| e.clone().run_images(a.inputs, a.place.into(), a.naming.into(), a.settings.into(), l))
            }
            "run_videos" => {
                let a: Run<dto::VideoSettings> = params(p)?;
                let planned = self.planned(&a.inputs, smaller_core::api::Kind::Video, &a.place);
                self.start(a.job, planned, |l| e.clone().run_videos(a.inputs, a.place.into(), a.naming.into(), a.settings.into(), l))
            }
            "run_pdfs" => {
                let a: Run<dto::PdfSettings> = params(p)?;
                let planned = self.planned(&a.inputs, a.settings.input_kind(), &a.place);
                self.start(a.job, planned, |l| e.clone().run_pdfs(a.inputs, a.place.into(), a.naming.into(), a.settings.into(), l))
            }
            "cancel" => {
                #[derive(Deserialize)]
                struct A {
                    job: u64,
                }
                let a: A = params(p)?;
                if let Some(job) = self.jobs.lock().unwrap_or_else(|e| e.into_inner()).get(&a.job) {
                    job.cancel();
                }
                Value::Null
            }
            "undo" => {
                // 与 Job::undo 同一个做法（jobs::undo）：输出移到回收站，新建的输出根目录一并移走。
                let a: UndoParams = params(p)?;
                let results: Vec<ItemResult> = a
                    .outputs
                    .into_iter()
                    .enumerate()
                    .map(|(index, path)| ItemResult {
                        index,
                        source: PathBuf::new(),
                        before_bytes: 0,
                        outputs: vec![OutputFile { path: path.into(), bytes: 0, width: 0, height: 0 }],
                        status: ItemStatus::Done,
                    })
                    .collect();
                let roots: Vec<PathBuf> = a.created_roots.into_iter().map(PathBuf::from).collect();
                jobs::undo(&results, &roots).map_err(|message| Failure { kind: "failed", message })?;
                Value::Null
            }
            "ping" => json!({ "protocol": PROTOCOL }),
            other => return Err(Failure { kind: "failed", message: format!("不认识的请求：{other}") }),
        })
    }

    /// 展开后会处理哪些文件、会新建哪些输出根目录（与任务里用同一套规划）。
    /// 应用靠它在引擎意外退出时把还没回报的文件标成失败，撤销时清掉新建的文件夹。
    fn planned(&self, inputs: &[String], kind: smaller_core::api::Kind, place: &dto::Place) -> Value {
        let inputs: Vec<PathBuf> = inputs.iter().map(PathBuf::from).collect();
        let place: smaller_core::api::Place = place.clone().into();
        let plan = jobs::plan(&inputs, kind.into(), &(&place).into());
        json!({
            "sources": plan.items.iter().map(|i| i.source.to_string_lossy().into_owned()).collect::<Vec<_>>(),
            "created_roots": plan.created_roots.iter().map(|r| r.to_string_lossy().into_owned()).collect::<Vec<_>>(),
        })
    }

    /// 起任务：先登记再开跑（锁住登记表，结束回调要等登记完才能移除，不会漏删）。
    fn start(&self, id: u64, planned: Value, run: impl FnOnce(Arc<dyn JobListener>) -> Arc<Job>) -> Value {
        let mut jobs = self.jobs.lock().unwrap_or_else(|e| e.into_inner());
        let listener: Arc<dyn JobListener> = Arc::new(Listener { job: id, jobs: self.jobs.clone() });
        let job = run(listener);
        if !job.is_finished() {
            jobs.insert(id, job);
        }
        planned
    }
}

/// 和应用之间的三条管道不让子进程（ffmpeg）继承：否则宿主被结束后 ffmpeg 还握着管道，应用读不到结尾，
/// 要等 ffmpeg 自己跑完才知道宿主没了。子进程需要的标准输入输出由 Command 另行复制，不受影响。
#[cfg(windows)]
fn keep_pipes_private() {
    use std::ffi::c_void;
    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn GetStdHandle(which: u32) -> *mut c_void;
        fn SetHandleInformation(handle: *mut c_void, mask: u32, flags: u32) -> i32;
    }
    const HANDLE_FLAG_INHERIT: u32 = 1;
    // STD_INPUT_HANDLE / STD_OUTPUT_HANDLE / STD_ERROR_HANDLE
    for which in [-10i32, -11, -12] {
        // SAFETY：只读标准句柄并改它的继承标志，句柄无效时两个调用都只返回失败。
        unsafe {
            let h = GetStdHandle(which as u32);
            if !h.is_null() && h as isize != -1 {
                SetHandleInformation(h, HANDLE_FLAG_INHERIT, 0);
            }
        }
    }
}

#[cfg(not(windows))]
fn keep_pipes_private() {}

fn main() {
    keep_pipes_private();
    // 引擎线程里的 panic 也报给应用写日志（默认只写到没人看的标准错误）。
    std::panic::set_hook(Box::new(|info| log("error", format!("引擎内部错误：{info}"))));

    let host = Arc::new(Host { engine: Engine::new(None), jobs: Arc::new(Mutex::new(HashMap::new())) });
    send(&json!({ "event": "ready", "protocol": PROTOCOL, "version": env!("CARGO_PKG_VERSION") }));

    let stdin = std::io::stdin();
    for line in stdin.lock().lines() {
        let Ok(line) = line else { break };
        // 个别写端会在开头带 UTF-8 BOM，去掉再解析。
        let line = line.trim_start_matches('\u{feff}');
        if line.trim().is_empty() {
            continue;
        }
        let request: Request = match serde_json::from_str(line) {
            Ok(r) => r,
            Err(e) => {
                log("error", format!("收到无法解析的请求：{e}"));
                continue;
            }
        };
        let host = host.clone();
        // 每个请求一个线程：读视频信息、解锁 PDF、剪贴板压缩这类慢请求不挡住别的。
        std::thread::spawn(move || {
            let outcome = catch_unwind(AssertUnwindSafe(|| host.handle(&request.method, &request.params)));
            let reply = match outcome {
                Ok(Ok(result)) => json!({ "id": request.id, "result": result }),
                Ok(Err(f)) => json!({ "id": request.id, "error": { "kind": f.kind, "message": f.message } }),
                Err(_) => json!({ "id": request.id, "error": { "kind": "failed", "message": "引擎内部错误" } }),
            };
            send(&reply);
        });
    }
    // 应用关了管道（退出或崩溃）：宿主跟着退出。正在跑的 ffmpeg 由应用的作业对象一并结束。
    std::process::exit(0);
}
