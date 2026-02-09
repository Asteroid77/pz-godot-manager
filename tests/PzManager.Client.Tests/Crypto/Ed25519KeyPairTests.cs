using PzManager.Client.Crypto;
using PzManager.Infrastructure.Crypto;
using Xunit;

namespace PzManager.Client.Tests.Crypto;

public sealed class Ed25519KeyPairTests
{
    [Fact]
    public void Sign_VerifiesWithServerVerifier()
    {
        var kp = Ed25519KeyPair.Generate();
        var msg = new byte[] { 1, 2, 3, 4, 5 };
        var sig = kp.Sign(msg);

        var verifier = new BouncyCastleEd25519SignatureVerifier();
        Assert.True(verifier.VerifyEd25519(kp.PublicKey, msg, sig));
    }
}

