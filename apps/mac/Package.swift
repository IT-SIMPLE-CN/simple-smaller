// swift-tools-version: 6.2
import PackageDescription
import Foundation

// 简单小一点 macOS 应用。界面 SwiftUI + AppKit，压缩全部交给 Rust 引擎（core/）。
// 先跑 scripts/build-core-mac.sh 生成绑定与静态库，再 swift build。
let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("../..").standardizedFileURL.path

// 链接用的 SDK 版本：打包脚本传 SMALLER_SDK_VERSION（xcrun --show-sdk-version），缺省按 26.0。
let sdkVersion = ProcessInfo.processInfo.environment["SMALLER_SDK_VERSION"] ?? "26.0"

let package = Package(
    name: "SimpleSmaller",
    platforms: [.macOS(.v14)],
    targets: [
        // UniFFI 生成的 C 头文件，模块名必须是 smaller_coreFFI，生成的 Swift 按这个名字 import。
        .systemLibrary(name: "smaller_coreFFI", path: "Sources/smaller_coreFFI"),
        .target(
            name: "SmallerCore",
            dependencies: ["smaller_coreFFI"],
            path: "Sources/SmallerCore",
            // 生成代码按 Swift 5 语义编，避免 Swift 6 严格并发检查报在生成文件上。
            swiftSettings: [.swiftLanguageMode(.v5)],
            // 直接给静态库的完整路径：用 -l 的话链接器会优先挑 target 目录里的同名 dylib。
            // jpegli 是 C++ 写的，要带上 C++ 标准库。
            linkerSettings: [.unsafeFlags(["\(root)/.local/lib/libsmaller_core.a", "-lc++"])]
        ),
        .executableTarget(
            name: "SimpleSmaller",
            dependencies: ["SmallerCore"],
            path: "Sources/SimpleSmaller",
            swiftSettings: [.swiftLanguageMode(.v5)],
            // SwiftPM 会把链接记录里的 SDK 版本写成最低系统版本（14.0），系统据此把应用当旧应用、
            // 不给 Liquid Glass。这里显式写明按新 SDK 链接；最低系统仍是 14。
            linkerSettings: [.unsafeFlags(["-Xlinker", "-platform_version", "-Xlinker", "macos", "-Xlinker", "14.0", "-Xlinker", sdkVersion])]
        ),
    ]
)
