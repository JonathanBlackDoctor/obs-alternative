using SilentStream.Core.Models;

namespace SilentStream.Core.Contracts;

/// <summary>
/// Encodes the captured A/V once and tees it to RTMP (onfail=ignore) and a local mp4
/// simultaneously via FFmpeg, with GPU auto-detection (NVENC→AMF→QSV→x264). See plan §3.5/§3.6.
/// </summary>
public interface IEncoderPipeline : IDisposable
{
    /// <summary>True while the FFmpeg process is running.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Time since the last ffmpeg progress line while the encoder is intended to be running
    /// (<see cref="TimeSpan.Zero"/> when stopped). Lets the watchdog catch a stalled feed where
    /// the process is alive but no longer emitting frames (e.g. RTMP slave failure / pipe EOF).
    /// </summary>
    TimeSpan TimeSinceProgress { get; }

    /// <summary>Raised on each metrics poll from the encoder (bitrate/fps).</summary>
    event EventHandler<MetricsSnapshot> MetricsUpdated;

    /// <summary>Raised when ffmpeg exits while it was intended to be running (crash/kill).</summary>
    event EventHandler? UnexpectedExit;

    /// <summary>Starts the tee pipeline (RTMP + mp4) with the given options.</summary>
    Task StartAsync(EncoderStartOptions options, CancellationToken ct);

    /// <summary>Gracefully stops the encoder (sends 'q' to finalise the mp4).</summary>
    Task StopAsync();
}
