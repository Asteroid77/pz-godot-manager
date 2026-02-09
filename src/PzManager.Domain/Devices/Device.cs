using PzManager.Domain.Security;

namespace PzManager.Domain.Devices;

public sealed record Device(
    DeviceId Id,
    string PublicKey,
    Role Role,
    string? Name,
    string? Note,
    bool Revoked,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenUtc);

