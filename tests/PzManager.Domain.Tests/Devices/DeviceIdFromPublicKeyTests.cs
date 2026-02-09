using PzManager.Domain.Devices;
using Xunit;

namespace PzManager.Domain.Tests.Devices;

public sealed class DeviceIdFromPublicKeyTests
{
    [Fact]
    public void FromEd25519PublicKey_IsDeterministic()
    {
        var pk = new byte[32];
        for (var i = 0; i < pk.Length; i++)
        {
            pk[i] = (byte)i;
        }

        var a = DeviceId.FromEd25519PublicKey(pk);
        var b = DeviceId.FromEd25519PublicKey(pk);

        Assert.Equal(a, b);
        Assert.False(string.IsNullOrWhiteSpace(a.Value));
    }

    [Fact]
    public void FromEd25519PublicKey_ThrowsOnInvalidLength()
    {
        var pk = new byte[31];
        Assert.Throws<ArgumentException>(() => DeviceId.FromEd25519PublicKey(pk));
    }
}

