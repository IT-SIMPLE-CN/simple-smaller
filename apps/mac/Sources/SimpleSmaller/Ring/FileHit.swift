import AppKit
import ApplicationServices
import UniformTypeIdentifiers

/// 从屏幕坐标找出指针下的文件或目录：访达窗口、桌面，以及其他在辅助功能里给出文件地址的应用。
/// 按图片、视频、PDF 与目录四类判断点中的文件。
///
/// 访达的图标、列表行里的文件名都带 `AXURL`（文件引用地址，要换成真实路径）。点在列表的
/// 「修改日期」这类其他列上时，命中的是没有地址的单元格，要先往上找到整行，再在行里找。
/// 这些都是跨进程的同步调用，超时压到 250ms，对方卡住也不会拖住主线程。
enum FileHit {
    private static let systemWide: AXUIElement = {
        let element = AXUIElementCreateSystemWide()
        AXUIElementSetMessagingTimeout(element, 0.25)
        return element
    }()

    static var isTrusted: Bool { AXIsProcessTrusted() }

    /// 弹出系统的辅助功能授权提示。已授权时什么都不做。
    static func requestTrust() {
        _ = AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue(): true] as CFDictionary)
    }

    /// 指针下的文件，以及它所在的多选。被点的文件在访达的选中项里、且选了不止一个时，
    /// `batch` 是整批选中的同类文件（按访达里的顺序，图片只带图片、视频只带视频、PDF 只带 PDF、目录只带目录）；否则只有它自己。
    struct Hit {
        let file: URL
        let kind: Kind
        let batch: [URL]
    }

    /// `point` 是 AppKit 的屏幕坐标（左下角为原点），这里换成辅助功能用的左上角坐标。
    /// 要在按下鼠标时调用：对已选中的文件点名字，访达松手就进入改名，多选也会在松手时收成一张。
    static func hit(at point: NSPoint) -> Hit? {
        guard let primary = NSScreen.screens.first else { return nil }
        var element: AXUIElement?
        guard AXUIElementCopyElementAtPosition(systemWide, Float(point.x), Float(primary.frame.height - point.y), &element) == .success,
              let element else { return nil }
        // 列表视图里 ⌥ 点展开三角是访达自己的「展开全部子目录」，不能被圆环抢走。
        if role(element) == kAXDisclosureTriangleRole as String { return nil }
        var current: AXUIElement? = element
        var file: URL?
        for _ in 0..<8 {
            // 访达窗口本身也带着当前目录的地址；往上找到窗口还没碰到文件，说明点的是工具栏或空白处，不算。
            guard let node = current, role(node) != kAXWindowRole as String else { break }
            if let url = fileURL(node) { file = url; break }
            if role(node) == kAXRowRole as String { file = descendantURL(node, depth: 3); break }
            current = parent(node)
        }
        guard let file, let kind = kind(of: file) else { return nil }
        let selected = selection(around: element).filter { self.kind(of: $0) == kind }
        return Hit(file: file, kind: kind, batch: selected.count > 1 && selected.contains(file) ? selected : [file])
    }

    /// 往上找到带选中项的容器：列表视图是 `AXSelectedRows`，图标视图与桌面是 `AXSelectedChildren`。
    private static func selection(around element: AXUIElement) -> [URL] {
        var current: AXUIElement? = element
        for _ in 0..<10 {
            guard let node = current else { break }
            for key in ["AXSelectedRows", kAXSelectedChildrenAttribute as String] {
                var value: AnyObject?
                if AXUIElementCopyAttributeValue(node, key as CFString, &value) == .success, let items = value as? [AXUIElement], !items.isEmpty {
                    return items.compactMap { fileURL($0) ?? descendantURL($0, depth: 3) }
                }
            }
            current = parent(node)
        }
        return []
    }

    /// 只认真实存在的图片、视频、PDF 与普通目录；App、照片库这类文件包，别名和其他类型都不弹圆环。
    enum Kind { case image, video, pdf, folder }

    static func kind(of url: URL) -> Kind? {
        guard let values = try? url.resourceValues(forKeys: [.isRegularFileKey, .isDirectoryKey, .isPackageKey, .contentTypeKey]) else { return nil }
        if values.isDirectory == true { return values.isPackage == true ? nil : .folder }
        guard values.isRegularFile == true, let type = values.contentType else { return nil }
        if type.conforms(to: .pdf) { return .pdf }
        if type.conforms(to: .image) { return .image }
        if type.conforms(to: .movie) { return .video }
        return nil
    }

    /// 对已选中的文件按 ⌥ 点在名字上，访达会同时进入改名；圆环收起后接着打字就会改掉文件名。
    /// 收起时若访达的焦点正是这个文件的改名框，只给访达发一个 Esc 取消改名，别的情况不动。
    static func cancelFinderRename(of url: URL) {
        guard let finder = NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.finder").first else { return }
        let app = AXUIElementCreateApplication(finder.processIdentifier)
        AXUIElementSetMessagingTimeout(app, 0.25)
        var focused: AnyObject?
        guard AXUIElementCopyAttributeValue(app, kAXFocusedUIElementAttribute as CFString, &focused) == .success,
              let focused, CFGetTypeID(focused) == AXUIElementGetTypeID() else { return }
        let field = focused as! AXUIElement
        var value: AnyObject?
        guard role(field) == kAXTextFieldRole as String,
              AXUIElementCopyAttributeValue(field, kAXValueAttribute as CFString, &value) == .success,
              value as? String == url.lastPathComponent else { return }
        for down in [true, false] {
            CGEvent(keyboardEventSource: nil, virtualKey: 53, keyDown: down)?.postToPid(finder.processIdentifier)
        }
    }

    private static func fileURL(_ element: AXUIElement) -> URL? {
        var value: AnyObject?
        guard AXUIElementCopyAttributeValue(element, kAXURLAttribute as CFString, &value) == .success,
              let url = value as? URL, url.isFileURL else { return nil }
        return (url as NSURL).filePathURL
    }

    private static func descendantURL(_ element: AXUIElement, depth: Int) -> URL? {
        guard depth > 0 else { return nil }
        for child in children(element) {
            if let url = fileURL(child) ?? descendantURL(child, depth: depth - 1) { return url }
        }
        return nil
    }

    private static func role(_ element: AXUIElement) -> String? {
        var value: AnyObject?
        guard AXUIElementCopyAttributeValue(element, kAXRoleAttribute as CFString, &value) == .success else { return nil }
        return value as? String
    }

    private static func parent(_ element: AXUIElement) -> AXUIElement? {
        var value: AnyObject?
        guard AXUIElementCopyAttributeValue(element, kAXParentAttribute as CFString, &value) == .success,
              let value, CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return (value as! AXUIElement)
    }

    private static func children(_ element: AXUIElement) -> [AXUIElement] {
        var value: AnyObject?
        guard AXUIElementCopyAttributeValue(element, kAXChildrenAttribute as CFString, &value) == .success else { return [] }
        return value as? [AXUIElement] ?? []
    }
}
