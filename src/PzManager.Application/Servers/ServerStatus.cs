namespace PzManager.Application.Servers;

public sealed record ServerStatus(ServerState State, string? Details = null);

