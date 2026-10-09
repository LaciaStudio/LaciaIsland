using System.Text;

namespace LaciaIsland.Services;

/// <summary>Tiny file logger (%APPDATA%\LaciaIsland\log.txt), never throws.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LaciaIsland");
    private static string FilePath => Path.Combine(Dir, "log.txt");
    public static bool ToConsole { get; set; }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Error(string where, Exception ex) => Write("ERR ", $"{where}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    public static void Warn(string msg) => Write("WARN", msg);

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {msg}\n";
        if (ToConsole) Console.Write(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 512 * 1024)
                {
                    var old = Path.Combine(Dir, "log.old.txt");
                    File.Copy(FilePath, old, true);
                    File.Delete(FilePath);
                }
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch { /* ignore */ }
    }
}
