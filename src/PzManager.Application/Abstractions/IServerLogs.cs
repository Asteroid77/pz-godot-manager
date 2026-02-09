using PzManager.Application.Servers;

namespace PzManager.Application.Abstractions;

public interface IServerLogs
{
    Task<IReadOnlyList<string>> TailAsync(int maxLines, CancellationToken cancellationToken);

    IAsyncEnumerable<ServerLogLine> FollowAsync(ServerLogsFollowOptions options, CancellationToken cancellationToken);
}
