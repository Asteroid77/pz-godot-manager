namespace PzManager.Transport.Protocol;

public sealed record PairingCreateRequestV1(
    string Role,
    int TtlSeconds);

