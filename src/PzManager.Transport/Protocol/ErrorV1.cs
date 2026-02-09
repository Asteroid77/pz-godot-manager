namespace PzManager.Transport.Protocol;

public sealed record ErrorV1(
    string Code,
    string Message,
    string? Details = null);

