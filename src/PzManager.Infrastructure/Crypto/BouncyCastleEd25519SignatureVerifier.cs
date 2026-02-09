using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using PzManager.Application.Abstractions;

namespace PzManager.Infrastructure.Crypto;

public sealed class BouncyCastleEd25519SignatureVerifier : ISignatureVerifier
{
    public bool VerifyEd25519(ReadOnlySpan<byte> publicKeyBytes, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (publicKeyBytes.Length != 32 || signature.Length != 64)
        {
            return false;
        }

        var signer = new Ed25519Signer();
        signer.Init(false, new Ed25519PublicKeyParameters(publicKeyBytes.ToArray(), 0));
        signer.BlockUpdate(message.ToArray(), 0, message.Length);
        return signer.VerifySignature(signature.ToArray());
    }
}

