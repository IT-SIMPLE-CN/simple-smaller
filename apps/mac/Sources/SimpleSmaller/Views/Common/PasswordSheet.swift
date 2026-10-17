import QuickLook
import SwiftUI

/// 加密 PDF 输入打开密码。密码只用来解出临时副本，不保存。
struct PasswordSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let item: FileItem
    @State private var password = ""
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(verbatim: "「\(item.name)」有打开密码").font(.system(size: 13, weight: .semibold))
            Text("输入密码后就能压缩、合并。密码只在这次处理里用，不会保存；输出的文件不带密码，需要的话在「选项」里另加。")
                .font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            SecureField("密码", text: $password)
                .textFieldStyle(.roundedBorder)
                .onSubmit(submit)
            if let error {
                Text(error).font(.system(size: 12)).foregroundStyle(Color.warning)
            }
            HStack {
                Spacer()
                Button("取消") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("解锁") { submit() }.keyboardShortcut(.defaultAction).disabled(password.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 380)
    }

    private func submit() {
        guard !password.isEmpty else { return }
        if let message = model.unlock(item, password: password) {
            error = message
        } else {
            dismiss()
        }
    }
}
