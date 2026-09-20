namespace FreedomMedia.App;

/// <summary>
/// Minimal always-on logger. Writes to %LOCALAPPDATA%\FreedomMedia\log.txt (or the OS equivalent),
/// flushing every line, so that even a hard native crash leaves the last step recorded. This is a
/// sideloaded application with no crash reporting behind it, so a written log is the only way to
/// learn why something failed on a user's machine.
/// </summary>
public static class AppLog
{
    private static readonly object Sync = new();
    public static string LogPath { get; }

    static AppLog()
    {
        string dir;
        try
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreedomMedia");
            Directory.CreateDirectory(dir);
        }
        catch
        {
            dir = Path.GetTempPath();
        }
        LogPath = Path.Combine(dir, "log.txt");
    }

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }

    public static void Exception(string context, Exception ex)
        => Write($"EXCEPTION in {context}: {ex.GetType().Name}: {ex.Message}\n{ex}");

    /// <summary>Wire process-wide handlers so unexpected failures are recorded.</summary>
    public static void InstallGlobalHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Exception("AppDomain.UnhandledException", ex);
            else Write($"AppDomain.UnhandledException (non-Exception): {e.ExceptionObject}");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Exception("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }
}
