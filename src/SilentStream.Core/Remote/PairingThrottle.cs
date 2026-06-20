namespace SilentStream.Core.Remote;

/// <summary>
/// Brute-force guard for the remote PIN-pairing endpoint (audit B3). The pairing PIN is only 6
/// digits (10^6) and was previously fixed for the server's lifetime with no rate limit, so an
/// attacker on the LAN (or who knows the tunnel URL) could try every PIN in minutes. This caps
/// failed attempts to <see cref="_maxAttempts"/> per window; on the Nth failure the caller locks
/// out further attempts for an exponentially escalating cooldown AND rotates the PIN, so any
/// progress an attacker made is discarded. A successful pairing resets the counter. Thread-safe;
/// the clock is injectable for tests.
/// </summary>
public sealed class PairingThrottle
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseLockout;
    private readonly TimeSpan _maxLockout;
    private readonly Func<DateTime> _now;

    private readonly object _gate = new();
    private int _failures;
    private int _lockoutLevel;
    private DateTime _lockedUntil = DateTime.MinValue;

    public PairingThrottle(
        int maxAttempts = 8,
        TimeSpan? baseLockout = null,
        TimeSpan? maxLockout = null,
        Func<DateTime>? now = null)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _baseLockout = baseLockout ?? TimeSpan.FromSeconds(30);
        _maxLockout = maxLockout ?? TimeSpan.FromMinutes(30);
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>True while pairing is locked out; <paramref name="retryAfter"/> is the time left.</summary>
    public bool IsLocked(out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            var remaining = _lockedUntil - _now();
            if (remaining > TimeSpan.Zero)
            {
                retryAfter = remaining;
                return true;
            }
            retryAfter = TimeSpan.Zero;
            return false;
        }
    }

    /// <summary>
    /// Records a failed PIN attempt. Returns true when this attempt tripped a (new) lockout —
    /// the caller should rotate the PIN and warn.
    /// </summary>
    public bool RecordFailure()
    {
        lock (_gate)
        {
            _failures++;
            if (_failures < _maxAttempts)
            {
                return false;
            }

            _failures = 0;
            _lockoutLevel++;
            var shift = Math.Min(_lockoutLevel - 1, 16); // bound the shift to avoid tick overflow
            var lockoutTicks = Math.Min(_maxLockout.Ticks, _baseLockout.Ticks << shift);
            _lockedUntil = _now() + TimeSpan.FromTicks(lockoutTicks);
            return true;
        }
    }

    /// <summary>Resets the counter after a successful pairing.</summary>
    public void RecordSuccess()
    {
        lock (_gate)
        {
            _failures = 0;
            _lockoutLevel = 0;
            _lockedUntil = DateTime.MinValue;
        }
    }
}
