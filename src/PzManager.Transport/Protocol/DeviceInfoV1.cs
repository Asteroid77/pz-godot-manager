namespace PzManager.Transport.Protocol;

public sealed record DeviceInfoV1(
    string DeviceId,
    string Role,
    string? Name,
    string? Note,
    bool Revoked,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenUtc);

