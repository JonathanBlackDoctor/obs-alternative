namespace SilentStream.Core;

/// <summary>
/// Well-known filesystem locations (plan §3.9 / §3.10). All app state lives under
/// %AppData%\SilentStream; recordings default to %USERPROFILE%\Videos\SilentStream.
/// </summary>
public static class AppPaths
{
    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SilentStream");

    public static string LogsDir => Path.Combine(AppDataDir, "logs");

    public static string ConfigFile => Path.Combine(AppDataDir, "config.json");

    /// <summary>OAuth client credentials file (never committed — see docs/CLAUDE.local.md.template).</summary>
    public static string ClientSecretFile => Path.Combine(AppDataDir, "client_secret.json");

    /// <summary>Scratch folder for per-period VOD cuts awaiting upload (확장계획서 §4.1).</summary>
    public static string VodDir => Path.Combine(AppDataDir, "vod");

    /// <summary>Persistent, quota-aware upload queue (확장계획서 §4.2). Not sensitive.</summary>
    public static string UploadQueueFile => Path.Combine(AppDataDir, "upload_queue.json");

    public static string DefaultRecordingFolder
    {
        get
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(videos))
            {
                videos = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            }
            return Path.Combine(videos, "SilentStream");
        }
    }
}
