namespace PzManager.Application.Abstractions;

public interface ISignatureVerifier
{
    bool VerifyEd25519(ReadOnlySpan<byte> publicKeyBytes, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature);
}

