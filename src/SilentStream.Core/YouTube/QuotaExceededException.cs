namespace SilentStream.Core.YouTube;

/// <summary>
/// Raised by <c>IYouTubeUploadService</c> when YouTube rejects an upload because the daily
/// API quota is spent (HTTP 403 quotaExceeded / dailyLimitExceeded). The upload queue keeps
/// the job pending and pauses its worker until the Pacific-time quota reset (확장계획서 §4.2, D7).
/// </summary>
public sealed class QuotaExceededException : Exception
{
    public QuotaExceededException(string message) : base(message) { }

    public QuotaExceededException(string message, Exception inner) : base(message, inner) { }
}
