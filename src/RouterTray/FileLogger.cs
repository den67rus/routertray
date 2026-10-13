using System.Diagnostics;
using System.Text;

namespace RouterTray;

internal sealed class FileLogger : IDisposable
{
    private readonly string _path;
    private readonly object _sync = new();

    public FileLogger(string path)
    {
        _path = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public void Info(string message)
    {
        Write("INFO", message, null);
    }

    public void Error(string message, Exception? ex = null)
    {
        Write("ERROR", message, ex);
    }

    private void Write(string level, string message, Exception? ex)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
        if (ex is not null)
        {
            line += Environment.NewLine + ex;
        }

        lock (_sync)
        {
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception writeEx)
            {
                Debug.WriteLine($"Failed to write log entry: {writeEx}");
            }
        }
    }

    public void Dispose()
    {
    }
}
