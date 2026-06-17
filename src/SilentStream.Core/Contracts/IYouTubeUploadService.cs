namespace SilentStream.Core.Contracts;

/// <summary>
/// Uploads a finished period VOD to YouTube via Data API v3 <c>videos.insert</c> (resumable
/// upload, tolerant of large files and network drops). Reuses the live service's stored OAuth
/// token — the existing full scope already covers videos.insert, so no re-login (확장계획서 §4.2, D9).
/// </summary>
public interface IYouTubeUploadService
{
    /// <summary>
    /// Uploads <paramref name="filePath"/> with the given title/privacy and returns the new
    /// video id. Throws <see cref="YouTube.QuotaExceededException"/> when the daily quota is spent
    /// so the queue can pause and retry after reset.
    /// </summary>
    Task<string> UploadAsync(string filePath, string title, string privacy, CancellationToken ct);
}
