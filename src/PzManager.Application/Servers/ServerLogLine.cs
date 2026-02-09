namespace PzManager.Application.Servers;

public sealed record ServerLogLine(string Text, string? TimestampUtc = null, long? Offset = null);
