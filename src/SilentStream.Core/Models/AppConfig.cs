using System.Text.Json.Serialization;

namespace SilentStream.Core.Models;

/// <summary>
/// Root application configuration persisted to %AppData%\SilentStream\config.json.
/// Mirrors the schema in plan §6. OAuth tokens are DPAPI-encrypted (Phase 1).
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// Schema version. v1 = base plan §6; v2 adds the period-VOD + remote sections
    /// (확장계획서 §6). Missing keys deserialize to their defaults, so an old v1 file
    /// loads cleanly and is bumped to v2 on next save.
    /// </summary>
    public int Version { get; set; } = 2;

    // camelCase would yield "youTube"; the documented schema (plan §6) uses "youtube".
    [JsonPropertyName("youtube")]
    public YouTubeConfig YouTube { get; set; } = new();

    public EncodingConfig Encoding { get; set; } = new();

    public AudioConfig Audio { get; set; } = new();

    public RecordingConfig Recording { get; set; } = new();

    /// <summary>Class-timetable + VOD settings (확장계획서 §6). Added in schema v2.</summary>
    public PeriodsConfig Periods { get; set; } = new();

    /// <summary>Smartphone remote-control server settings (확장계획서 §6). Added in schema v2.</summary>
    public RemoteConfig Remote { get; set; } = new();

    /// <summary>Global hotkey that toggles the control UI (plan §3.8).</summary>
    public string Hotkey { get; set; } = "Ctrl+Shift+F12";

    /// <summary>Auto-start mechanism: "startup" (registry Run) or "scheduler" (Task Scheduler).</summary>
    public string Autostart { get; set; } = "startup";

    /// <summary>Returns a fresh config populated with the documented defaults.</summary>
    public static AppConfig CreateDefault() => new();
}

public sealed class YouTubeConfig
{
    /// <summary>DPAPI-encrypted refresh token (base64). Empty until first login.</summary>
    public string RefreshTokenEnc { get; set; } = string.Empty;

    /// <summary>Broadcast privacy. Plan fixes this to "unlisted".</summary>
    public string Privacy { get; set; } = "unlisted";

    /// <summary>Title template; {0:...}/format tokens applied at session start.</summary>
    public string TitleTemplate { get; set; } = "라이브 - {yyyy-MM-dd HH:mm}";
}

public sealed class EncodingConfig
{
    /// <summary>"auto" | "nvenc" | "amf" | "qsv" | "x264".</summary>
    public string PreferredGpu { get; set; } = "auto";

    /// <summary>"source" follows the primary monitor.</summary>
    public string Resolution { get; set; } = "source";

    /// <summary>"source" follows the primary monitor refresh rate.</summary>
    public string Fps { get; set; } = "source";

    /// <summary>"25" | "50" | "75" | "none" — approximate resource cap (plan §3.5).</summary>
    public string ResourceLimit { get; set; } = "none";
}

public sealed class AudioConfig
{
    /// <summary>System (loopback) volume, 0.0-1.0.</summary>
    public double SystemVolume { get; set; } = 1.0;

    /// <summary>Microphone volume, 0.0-1.0.</summary>
    public double MicVolume { get; set; } = 1.0;

    /// <summary>Selected microphone device id, null = system default.</summary>
    public string? MicDeviceId { get; set; }
}

public sealed class RecordingConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Target folder. Default %USERPROFILE%\Videos\SilentStream (resolved at runtime).</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>Capacity cap in GB; oldest files deleted when exceeded (plan §3.6).</summary>
    public int MaxSizeGb { get; set; } = 100;

    /// <summary>Retention window in days; older files auto-deleted.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Minimum free space in GB before recording is paused (plan §3.6).</summary>
    public int MinFreeGb { get; set; } = 5;
}

/// <summary>
/// Class-timetable + per-period VOD settings (확장계획서 §6). Weekday defaults are keyed by
/// the 3-letter English weekday abbreviation ("Mon".."Sun"); per-date overrides are keyed by
/// ISO date ("yyyy-MM-dd"). Empty maps simply mean "no periods → nothing is cut/uploaded".
/// </summary>
public sealed class PeriodsConfig
{
    /// <summary>Per-weekday default schedules, keyed "Mon".."Sun".</summary>
    public Dictionary<string, List<PeriodEntry>> WeekdayDefaults { get; set; } = new();

    /// <summary>Per-date overrides, keyed "yyyy-MM-dd" (D5: 그날 통째 덮어쓰기).</summary>
    public Dictionary<string, List<PeriodEntry>> Overrides { get; set; } = new();

    /// <summary>Title template for uploaded period VODs. {교시}/{교시:00} + date tokens (D6).</summary>
    public string TitleTemplate { get; set; } = "{교시}교시 - {yyyy-MM-dd}";

    /// <summary>VOD privacy; plan fixes this to "unlisted" (D4).</summary>
    public string VodPrivacy { get; set; } = "unlisted";

    /// <summary>"immediate-throttled" (default, D10) or "after-hours".</summary>
    public string UploadTiming { get; set; } = "immediate-throttled";
}

/// <summary>One timetable row as stored in config.json: {"no":1,"start":"09:00:00","end":"09:50:00"}.</summary>
public sealed class PeriodEntry
{
    /// <summary>1-based period number (1교시, 2교시 …). Serializes as "no".</summary>
    [JsonPropertyName("no")]
    public int No { get; set; }

    /// <summary>Local start time, "HH:mm:ss".</summary>
    public string Start { get; set; } = "00:00:00";

    /// <summary>Local end time, "HH:mm:ss".</summary>
    public string End { get; set; } = "00:00:00";
}

/// <summary>Smartphone remote-control server settings (확장계획서 §6, D8/D11).</summary>
public sealed class RemoteConfig
{
    /// <summary>"off" (default), "lan", or "tailscale" — see RemoteBindMode.</summary>
    public string Mode { get; set; } = "off";

    /// <summary>TCP port the embedded Kestrel server binds to.</summary>
    public int Port { get; set; } = 8787;

    /// <summary>SHA-256 hashes of paired device tokens (D11). Never stores raw tokens.</summary>
    public List<string> DeviceTokens { get; set; } = new();
}
