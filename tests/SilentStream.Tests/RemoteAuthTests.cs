using SilentStream.Core.Remote;
using Xunit;

namespace SilentStream.Tests;

public class RemoteAuthTests
{
    [Fact]
    public void New_pin_is_six_digits()
    {
        for (var i = 0; i < 50; i++)
        {
            var pin = RemoteAuth.NewPin();
            Assert.Equal(6, pin.Length);
            Assert.All(pin, c => Assert.True(char.IsDigit(c)));
        }
    }

    [Fact]
    public void Tokens_are_unique_and_hash_is_stable()
    {
        var a = RemoteAuth.NewToken();
        var b = RemoteAuth.NewToken();
        Assert.NotEqual(a, b);
        Assert.Equal(RemoteAuth.HashToken(a), RemoteAuth.HashToken(a)); // deterministic
        Assert.NotEqual(RemoteAuth.HashToken(a), RemoteAuth.HashToken(b));
    }

    [Fact]
    public void Hash_does_not_reveal_the_raw_token()
    {
        var token = RemoteAuth.NewToken();
        var hash = RemoteAuth.HashToken(token);
        Assert.DoesNotContain(token, hash);
        Assert.Equal(64, hash.Length); // sha256 hex
    }

    [Fact]
    public void Known_token_recognised_only_after_pairing()
    {
        var token = RemoteAuth.NewToken();
        var known = new List<string>();

        Assert.False(RemoteAuth.IsKnownToken(known, token)); // not paired yet
        known.Add(RemoteAuth.HashToken(token));              // pair
        Assert.True(RemoteAuth.IsKnownToken(known, token));
        Assert.False(RemoteAuth.IsKnownToken(known, RemoteAuth.NewToken())); // a different device
        Assert.False(RemoteAuth.IsKnownToken(known, null));
        Assert.False(RemoteAuth.IsKnownToken(known, ""));
    }

    [Theory]
    [InlineData("123456", "123456", true)]
    [InlineData("123456", "123457", false)]
    [InlineData("123456", "12345", false)] // different length
    [InlineData("", "", true)]
    public void Constant_time_equals_matches_value_and_length(string a, string b, bool expected)
    {
        Assert.Equal(expected, RemoteAuth.ConstantTimeEquals(a, b));
    }

    [Fact]
    public void Constant_time_equals_handles_null()
    {
        Assert.False(RemoteAuth.ConstantTimeEquals(null, "x"));
        Assert.False(RemoteAuth.ConstantTimeEquals("x", null));
    }
}

public class PairingThrottleTests
{
    private sealed class Clock
    {
        public DateTime Now = new(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
    }

    [Fact]
    public void Not_locked_until_max_attempts_reached()
    {
        var clock = new Clock();
        var t = new PairingThrottle(maxAttempts: 3, baseLockout: TimeSpan.FromSeconds(30), now: () => clock.Now);

        Assert.False(t.IsLocked(out _));
        Assert.False(t.RecordFailure()); // 1
        Assert.False(t.RecordFailure()); // 2
        Assert.False(t.IsLocked(out _));
        Assert.True(t.RecordFailure());  // 3 → trips the lockout
        Assert.True(t.IsLocked(out var retry));
        Assert.True(retry > TimeSpan.Zero);
    }

    [Fact]
    public void Lockout_expires_after_the_cooldown()
    {
        var clock = new Clock();
        var t = new PairingThrottle(maxAttempts: 1, baseLockout: TimeSpan.FromSeconds(30), now: () => clock.Now);

        Assert.True(t.RecordFailure()); // locks immediately
        Assert.True(t.IsLocked(out _));
        clock.Now = clock.Now.AddSeconds(31);
        Assert.False(t.IsLocked(out _));
    }

    [Fact]
    public void Success_resets_the_failure_counter()
    {
        var clock = new Clock();
        var t = new PairingThrottle(maxAttempts: 3, now: () => clock.Now);

        t.RecordFailure();
        t.RecordFailure(); // 2 failures
        t.RecordSuccess();

        Assert.False(t.RecordFailure()); // counter reset → 1, still below the cap
        Assert.False(t.RecordFailure()); // 2
        Assert.False(t.IsLocked(out _));
    }

    [Fact]
    public void Repeated_lockouts_escalate_the_cooldown()
    {
        var clock = new Clock();
        var t = new PairingThrottle(maxAttempts: 1, baseLockout: TimeSpan.FromSeconds(30),
            maxLockout: TimeSpan.FromHours(1), now: () => clock.Now);

        Assert.True(t.RecordFailure());
        t.IsLocked(out var first);
        clock.Now = clock.Now.Add(first).AddSeconds(1); // wait out the first lockout

        Assert.True(t.RecordFailure());
        t.IsLocked(out var second);
        Assert.True(second > first); // exponential escalation
    }
}
