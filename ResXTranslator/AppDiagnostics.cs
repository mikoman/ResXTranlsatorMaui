using System.Diagnostics;

namespace ResXTranslator;

/// <summary>
/// Writes small, redacted lifecycle records to the debugger and a local log.
/// Callers must only provide metadata: never credentials, headers, prompts, or
/// source/translated string content.
/// </summary>
static class AppDiagnostics
{
    const long MaxLogBytes = 1_000_000;
    static readonly object Sync = new();
    static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];
    static bool _initialized;

    public static string LogPath => Path.Combine(FileSystem.AppDataDirectory, "Logs", "resxtranslator.log");

    public static void Write(string category, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{SessionId}] [{category}] {SingleLine(message)}";
        Debug.WriteLine(line);
        Trace.WriteLine(line);

        try
        {
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(directory);

                if (!_initialized)
                {
                    RotateIfNeeded();
                    File.AppendAllText(LogPath, $"{Environment.NewLine}--- ResXTranslator session {SessionId} ---{Environment.NewLine}");
                    _initialized = true;
                }

                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never interrupt translation or replace its real
            // error with a logging failure. Debug/Trace still received the line.
        }
    }

    public static void WriteException(string category, string context, Exception exception) =>
        Write(
            category,
            $"{context} | {exception.GetType().Name}: {exception.Message}");

    public static void EnsureLogExists() => Write("Diagnostics", "Log opened by user");

    static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes)
        {
            return;
        }

        var previousPath = Path.Combine(Path.GetDirectoryName(LogPath)!, "resxtranslator.previous.log");
        File.Move(LogPath, previousPath, overwrite: true);
    }

    static string SingleLine(string value) => string.Join(
        " ",
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
