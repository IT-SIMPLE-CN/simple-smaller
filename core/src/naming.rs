//! 输出文件命名：模板变量、拼音、小写、连字符与重名加序号。
//!
//! 模板变量：`{name}` 原文件名（不含扩展名）、`{w}` `{h}` 输出宽高、`{ratio}` 比例（如 3x4）、
//! `{date}` 日期（20261016）、`{n}` 批次内序号（从 1 起）、`{folder}` 所在文件夹名。
//! 不认识的 `{...}` 原样保留。

use std::collections::HashSet;
use std::path::{Path, PathBuf};

use pinyin::ToPinyin;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NamingRule {
    pub template: String,
    pub lowercase: bool,
    /// 中文转拼音，每个字一个音节，用连字符隔开。
    pub pinyin: bool,
    /// 空格与特殊字符换成连字符，适合直接当网址用。
    pub hyphenate: bool,
}

impl Default for NamingRule {
    fn default() -> Self {
        Self { template: "{name}-small".into(), lowercase: false, pinyin: false, hyphenate: false }
    }
}

#[derive(Debug, Clone, Default)]
pub struct NameVars {
    pub name: String,
    pub width: u32,
    pub height: u32,
    pub ratio: Option<String>,
    pub date: String,
    pub index: usize,
    pub folder: String,
}

fn is_han(c: char) -> bool {
    matches!(c as u32, 0x3400..=0x4DBF | 0x4E00..=0x9FFF | 0xF900..=0xFAFF | 0x20000..=0x2FA1F)
}

fn to_pinyin(s: &str) -> String {
    let mut out = String::new();
    let mut prev_han = false;
    for c in s.chars() {
        if let Some(p) = c.to_pinyin() {
            if !out.is_empty() && !out.ends_with('-') {
                out.push('-');
            }
            out.push_str(p.plain());
            prev_han = true;
        } else {
            if prev_han && c.is_alphanumeric() {
                out.push('-');
            }
            out.push(c);
            prev_han = is_han(c);
        }
    }
    out
}

/// 换连字符：字母数字与中文保留，其余（空格、下划线、符号）合并成一个连字符，去掉首尾。
fn hyphenate(s: &str) -> String {
    let mut out = String::new();
    for c in s.chars() {
        if c.is_alphanumeric() || c == '.' {
            out.push(c);
        } else if !out.ends_with('-') {
            out.push('-');
        }
    }
    out.trim_matches('-').to_string()
}

/// 文件名里不能出现的字符一律换掉；空名退回 `file`。
/// 按两端都合法来定：Windows 不许 `<>:"/\\|?*`、结尾的点和空格，以及 CON、NUL、COM1 这类设备名。
fn sanitize(s: &str) -> String {
    let cleaned: String = s
        .chars()
        .map(|c| if matches!(c, '/' | '\\' | ':' | '<' | '>' | '"' | '|' | '?' | '*' | '\0') || c.is_control() { '-' } else { c })
        .collect();
    let cleaned = cleaned.trim().trim_start_matches('.').trim_end_matches(['.', ' ']).to_string();
    if cleaned.is_empty() {
        return "file".into();
    }
    if is_device_name(&cleaned) { format!("{cleaned}-") } else { cleaned }
}

/// Windows 保留的设备名（不分大小写，带不带扩展名都不行）。
fn is_device_name(s: &str) -> bool {
    let stem = s.split('.').next().unwrap_or(s).to_ascii_uppercase();
    matches!(stem.as_str(), "CON" | "PRN" | "AUX" | "NUL")
        || (stem.len() == 4
            && (stem.starts_with("COM") || stem.starts_with("LPT"))
            && stem.as_bytes()[3].is_ascii_digit()
            && stem.as_bytes()[3] != b'0')
}

/// 按规则生成文件名（不含扩展名）。
pub fn render(rule: &NamingRule, vars: &NameVars) -> String {
    let mut out = String::new();
    let mut rest = rule.template.as_str();
    while let Some(start) = rest.find('{') {
        out.push_str(&rest[..start]);
        let Some(len) = rest[start..].find('}') else {
            out.push_str(&rest[start..]);
            rest = "";
            break;
        };
        let key = &rest[start + 1..start + len];
        let value = match key {
            "name" => Some(vars.name.clone()),
            "w" => Some(vars.width.to_string()),
            "h" => Some(vars.height.to_string()),
            "ratio" => Some(vars.ratio.clone().unwrap_or_else(|| "orig".into())),
            "date" => Some(vars.date.clone()),
            "n" => Some(vars.index.to_string()),
            "folder" => Some(vars.folder.clone()),
            _ => None,
        };
        match value {
            Some(v) => out.push_str(&v),
            None => out.push_str(&rest[start..=start + len]),
        }
        rest = &rest[start + len + 1..];
    }
    out.push_str(rest);

    let mut name = out;
    if rule.pinyin {
        name = to_pinyin(&name);
    }
    if rule.hyphenate {
        name = hyphenate(&name);
    }
    if rule.lowercase {
        name = name.to_lowercase();
    }
    sanitize(&name)
}

/// 在 `dir` 里给 `stem.ext` 找一个没被占用的路径：已有文件或本批已预留的都算占用，
/// 依次试 `stem-2.ext`、`stem-3.ext`……
pub fn unique_path(dir: &Path, stem: &str, ext: &str, reserved: &mut HashSet<PathBuf>) -> PathBuf {
    let make = |s: &str| if ext.is_empty() { dir.join(s) } else { dir.join(format!("{s}.{ext}")) };
    let mut candidate = make(stem);
    let mut n = 2;
    while candidate.exists() || reserved.contains(&candidate) {
        candidate = make(&format!("{stem}-{n}"));
        n += 1;
    }
    reserved.insert(candidate.clone());
    candidate
}

/// 同一个名字配几种扩展名（如实况的 `.jpg` + `.mov`）：找一个每种扩展名都没被占用的名字，
/// 依次试 `stem`、`stem-2`……返回名字（不含扩展名），并把这几个路径都预留。
pub fn unique_stem(dir: &Path, stem: &str, exts: &[&str], reserved: &mut HashSet<PathBuf>) -> String {
    let taken = |s: &str, reserved: &HashSet<PathBuf>| exts.iter().any(|e| { let p = dir.join(format!("{s}.{e}")); p.exists() || reserved.contains(&p) });
    let mut candidate = stem.to_string();
    let mut n = 2;
    while taken(&candidate, reserved) {
        candidate = format!("{stem}-{n}");
        n += 1;
    }
    for e in exts {
        reserved.insert(dir.join(format!("{candidate}.{e}")));
    }
    candidate
}

/// 宽高化成最简比例，写成 `3x4`。
pub fn ratio_label(width: u32, height: u32) -> String {
    fn gcd(a: u32, b: u32) -> u32 {
        if b == 0 { a } else { gcd(b, a % b) }
    }
    let g = gcd(width, height).max(1);
    format!("{}x{}", width / g, height / g)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn vars(name: &str) -> NameVars {
        NameVars { name: name.into(), width: 2560, height: 1440, ratio: None, date: "20261016".into(), index: 3, folder: "秋季".into() }
    }

    #[test]
    fn 模板变量替换() {
        let rule = NamingRule { template: "{name}-{w}x{h}-{n}-{date}-{x}".into(), ..Default::default() };
        assert_eq!(render(&rule, &vars("hero")), "hero-2560x1440-3-20261016-{x}");
    }

    #[test]
    fn 中文转拼音小写连字符() {
        let rule = NamingRule { template: "{name}-{w}".into(), lowercase: true, pinyin: true, hyphenate: true };
        assert_eq!(render(&rule, &vars("新品发布 Banner_01")), "xin-pin-fa-bu-banner-01-2560");
        assert_eq!(render(&rule, &vars("UNISKIN新品")), "uniskin-xin-pin-2560");
    }

    #[test]
    fn 路径字符被替换() {
        let rule = NamingRule { template: "{name}".into(), ..Default::default() };
        assert_eq!(render(&rule, &vars("a/b:c")), "a-b-c");
        assert_eq!(render(&rule, &vars("")), "file");
        assert_eq!(render(&rule, &vars("问<答>?*|\"x\". ")), "问-答-----x-");
        assert_eq!(render(&rule, &vars("con")), "con-");
        assert_eq!(render(&rule, &vars("COM1")), "COM1-");
        assert_eq!(render(&rule, &vars("COM0")), "COM0");
        assert_eq!(render(&rule, &vars("console")), "console");
    }

    #[test]
    fn 重名加序号() {
        let dir = std::env::temp_dir().join(format!("smaller-naming-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(dir.join("a.jpg"), b"x").unwrap();
        let mut reserved = HashSet::new();
        assert_eq!(unique_path(&dir, "a", "jpg", &mut reserved), dir.join("a-2.jpg"));
        assert_eq!(unique_path(&dir, "a", "jpg", &mut reserved), dir.join("a-3.jpg"));
        assert_eq!(unique_path(&dir, "b", "jpg", &mut reserved), dir.join("b.jpg"));
    }

    #[test]
    fn 比例化简() {
        assert_eq!(ratio_label(1242, 1656), "3x4");
        assert_eq!(ratio_label(1920, 1080), "16x9");
    }
}
