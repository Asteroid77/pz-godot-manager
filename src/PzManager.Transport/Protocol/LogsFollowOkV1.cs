namespace PzManager.Transport.Protocol;

public sealed record LogsFollowOkV1(
    string FollowId,
    string[] Lines,
    bool IsEnd,
    string? Reason,
    long? NextOffset = null,
    string? NextSince = null);
