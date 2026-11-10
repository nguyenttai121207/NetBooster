namespace NetBooster.Services;

public static class AppPaths
{
    public static string UserDataDirectory => Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetBooster"));
    // Creating this location is delayed until an elevated configuration change is about to be backed up.
    public static string CommonDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetBooster");
    public static string BackupDirectory => Ensure(Path.Combine(CommonDataDirectory, "backups"));
    public static string SettingsFilePath => Path.Combine(UserDataDirectory, "settings.json");
    private static string Ensure(string path) { Directory.CreateDirectory(path); return path; }
}
