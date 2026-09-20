using Avalonia;

namespace FreedomMedia.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppLog.InstallGlobalHandlers();
        AppLog.Write($"FreedomMedia starting (OS: {Environment.OSVersion}, 64-bit: {Environment.Is64BitProcess})");
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLog.Exception("Program.Main", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
