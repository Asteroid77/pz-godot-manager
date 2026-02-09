namespace PzManager.Transport.Protocol;

public sealed record DeviceUpdateRequestV1(
    string DeviceId,
    string? Role,
    string? Note);

