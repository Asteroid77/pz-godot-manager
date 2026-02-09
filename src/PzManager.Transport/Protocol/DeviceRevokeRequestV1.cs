namespace PzManager.Transport.Protocol;

public sealed record DeviceRevokeRequestV1(
    string DeviceId,
    string? Note);

