namespace PzManager.Transport.Protocol;

public sealed record LogsFollowRequestV1(int TailLines, int BatchLines, string? Since = null, long? Offset = null);
