using System.Text;

namespace NetBooster.Services;

public sealed class AppLogger
{
    private readonly string _path;
    public AppLogger()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetBooster");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "operations.log");
    }
    public void Info(string message) => Write("INFO", message);
    public void Error(string message) => Write("ERROR", message);
    private void Write(string level, string message)
    {
        // Never log command arguments containing a WireGuard configuration or private key.
        File.AppendAllText(_path, $"{DateTime.Now:O} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
    }
    public string ReadAll() => File.Exists(_path) ? File.ReadAllText(_path) : "Chưa có thao tác nào.";
}
