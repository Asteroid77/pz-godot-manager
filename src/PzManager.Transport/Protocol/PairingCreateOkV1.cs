namespace PzManager.Transport.Protocol;

public sealed record PairingCreateOkV1(
    string PairingCode,
    DateTimeOffset ExpiresAtUtc);

