namespace SilentStream.Core.Media;

/// <summary>
/// Classifies WASAPI capture endpoints by friendly name. A "loopback" capture device is really a
/// monitor of the system output (Stereo Mix / What U Hear / Wave Out Mix / 스테레오 믹스). It must
/// never be auto-selected as the microphone: doing so makes the mic leg duplicate system audio and
/// the real mic is never captured (the 1st field-test S9 root cause). Name-based because Windows
/// exposes these as ordinary capture endpoints with no API flag distinguishing them.
/// </summary>
public static class AudioDeviceClassifier
{
    private static readonly string[] LoopbackMarkers =
    [
        "stereo mix",
        "스테레오 믹스",
        "스테레오믹스",
        "what u hear",
        "what you hear",
        "wave out mix",
        "wave-out mix",
        "loopback",
        "rec. playback",
        "녹음 재생",
    ];

    /// <summary>True when the device friendly name looks like a system-output loopback monitor.</summary>
    public static bool IsLoopbackName(string? friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return false;
        }
        foreach (var marker in LoopbackMarkers)
        {
            if (friendlyName.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
