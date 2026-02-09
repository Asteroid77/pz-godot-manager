namespace PzManager.Application.Servers;

public sealed record ServerLogsFollowOptions(int TailLines, string? Since = null, long? Offset = null);
