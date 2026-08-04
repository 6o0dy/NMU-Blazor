namespace NMU.Platform.Components.Services;

public static class CacheDiagnostics
{
    private static readonly List<string> Ring = new();
    private const int MaxLines = 300;
    private static readonly object Lock = new();

    public static string BasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NMU", "MediaCache");

    public static string LogPath => Path.Combine(BasePath, "log.txt");

    public static void Log(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
        lock (Lock)
        {
            Ring.Add(line);
            if (Ring.Count > MaxLines)
                Ring.RemoveRange(0, Ring.Count - MaxLines);
            try
            {
                Directory.CreateDirectory(BasePath);
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { }
        }
    }

    public static string GetRecentLines(int count)
    {
        lock (Lock)
        {
            var start = Math.Max(0, Ring.Count - count);
            return string.Join(Environment.NewLine, Ring.Skip(start));
        }
    }

    public static void Clear()
    {
        lock (Lock)
        {
            Ring.Clear();
            try { File.WriteAllText(LogPath, ""); } catch { }
        }
    }
}
