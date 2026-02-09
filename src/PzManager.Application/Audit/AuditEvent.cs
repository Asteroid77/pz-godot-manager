namespace PzManager.Application.Audit;

public sealed record AuditEvent(
    DateTimeOffset TimeUtc,
    string ActorDeviceId,
    string ActorRole,
    string Action,
    string? TargetId = null,
    Dictionary<string, string>? Data = null);

