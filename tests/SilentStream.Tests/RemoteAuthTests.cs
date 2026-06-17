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
}
