namespace PzManager.Transport.Protocol;

public sealed record PairingBundleV1(
    string ManagerUrl,
    string PairingCode,
    string Role,
    DateTimeOffset ExpiresAtUtc,
    string? OverlayKind = null,
    string? OverlayJoinToken = null,
    string? OverlayHint = null);

