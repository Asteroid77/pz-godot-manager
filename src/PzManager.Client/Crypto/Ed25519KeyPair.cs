using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace PzManager.Client.Crypto;

public sealed record Ed25519KeyPair(byte[] PublicKey, byte[] PrivateKey)
{
    public static Ed25519KeyPair Generate()
    {
        var gen = new Ed25519KeyPairGenerator();
        gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        AsymmetricCipherKeyPair kp = gen.GenerateKeyPair();
        var priv = (Ed25519PrivateKeyParameters)kp.Private;
        var pub = (Ed25519PublicKeyParameters)kp.Public;
        return new Ed25519KeyPair(pub.GetEncoded(), priv.GetEncoded());
    }

    public byte[] Sign(ReadOnlySpan<byte> message)
    {
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(PrivateKey, 0));
        signer.BlockUpdate(message);
        return signer.GenerateSignature();
    }
}

