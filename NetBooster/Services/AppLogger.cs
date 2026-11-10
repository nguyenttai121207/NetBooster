using System.Text;

namespace NetBooster.Services;

public sealed class AppLogger
{
    private const long MaximumBytes = 5 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly string _path;

    public AppLogger() => _path = Path.Combine(AppPaths.UserDataDirectory, "operations.log");
    public void Info(string message) => Write("INFO", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        // Only operation names and error summaries belong here. Never pass passwords, keys, URLs with credentials, or file contents.
        var line = $"{DateTime.Now:O} [{level}] {message.Replace(Environment.NewLine, " ")}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length + Encoding.UTF8.GetByteCount(line) > MaximumBytes) Rotate();
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch { /* Logging must not prevent a network safety operation. */ }
        }
    }

    private void Rotate()
    {
        var second = _path + ".2"; var first = _path + ".1";
        if (File.Exists(second)) File.Delete(second);
        if (File.Exists(first)) File.Move(first, second);
        if (File.Exists(_path)) File.Move(_path, first);
    }

    public string ReadAll()
    {
        lock (_gate)
        {
            var parts = new[] { _path + ".2", _path + ".1", _path }.Where(File.Exists).Select(File.ReadAllText);
            return string.Join(Environment.NewLine, parts.DefaultIfEmpty("Chưa có thao tác nào."));
        }
    }
}
