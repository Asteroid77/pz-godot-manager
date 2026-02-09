namespace PzManager.Transport.Protocol;

public sealed record AuthRequestV1(
    string PublicKey,
    string Signature,
    string? PairingCode,
    string? DeviceName);

