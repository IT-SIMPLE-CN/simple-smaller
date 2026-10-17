namespace SimpleSmaller;

/// 崩溃日志：追加到 %LOCALAPPDATA%\SimpleSmaller\crash.log，超过 1 MB 从头写。不收集、不上传。
internal static class CrashLog
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleSmaller", "crash.log");

    public static void Write(string where, Exception? e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000) File.Delete(FilePath);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where} {typeof(CrashLog).Assembly.GetName().Version}\n{e}\n\n");
        }
        catch { }
    }
}
