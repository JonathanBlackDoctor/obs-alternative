namespace SilentStream.Core.Contracts;

/// <summary>
/// Persistent, quota-aware upload queue (확장계획서 §4.2). Jobs survive reboots (JSON on disk),
/// the worker uploads at most ~6/day and pauses on quota exhaustion, resuming after the
/// Pacific-time reset so 7교시+ overflow uploads the next day (D7).
/// </summary>
public interface IUploadQueue
{
    /// <summary>Adds a job and persists it immediately, then nudges the worker.</summary>
    void Enqueue(UploadJob job);

    /// <summary>Current snapshot of all jobs (pending, uploading, completed, failed).</summary>
    IReadOnlyList<UploadJob> Snapshot();

    /// <summary>Starts the background worker. Idempotent; stops on <paramref name="ct"/>.</summary>
    void Start(CancellationToken ct);
}

/// <summary>
/// One queued upload (확장계획서 §5). Immutable; the queue replaces entries with <c>with</c>
/// expressions as status/attempts/videoId change.
/// </summary>
/// <param name="Id">Stable unique id.</param>
/// <param name="FilePath">Absolute path of the cut mp4 to upload.</param>
/// <param name="Title">Upload title (e.g. "1교시 - 2026-06-14").</param>
/// <param name="Date">The class date this period belongs to.</param>
/// <param name="PeriodNumber">1-based period number.</param>
/// <param name="Status">See <see cref="UploadJobStatus"/>.</param>
/// <param name="Attempts">Number of failed upload attempts so far.</param>
/// <param name="VideoId">Resulting YouTube video id once completed, else null.</param>
public sealed record UploadJob(
    string Id,
    string FilePath,
    string Title,
    DateOnly Date,
    int PeriodNumber,
    string Status,
    int Attempts,
    string? VideoId);

/// <summary>Upload job lifecycle states persisted in the queue file.</summary>
public static class UploadJobStatus
{
    public const string Pending = "pending";
    public const string Uploading = "uploading";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
