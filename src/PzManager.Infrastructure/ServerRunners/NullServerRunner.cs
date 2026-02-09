using System.Runtime.CompilerServices;
using PzManager.Application.Abstractions;
using PzManager.Application.Errors;
using PzManager.Application.Servers;

namespace PzManager.Infrastructure.ServerRunners;

public sealed class NullServerRunner : IServerRunner, IServerLogs
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        Task.FromException(new AppException(AppErrorCodes.InternalError, "server runner disabled"));

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.FromException(new AppException(AppErrorCodes.InternalError, "server runner disabled"));

    public Task RestartAsync(CancellationToken cancellationToken) =>
        Task.FromException(new AppException(AppErrorCodes.InternalError, "server runner disabled"));

    public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new ServerStatus(ServerState.Unknown, "runner disabled"));
    }

    public Task<IReadOnlyList<string>> TailAsync(int maxLines, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<string>>(new AppException(AppErrorCodes.InternalError, "server runner disabled"));

    public async IAsyncEnumerable<ServerLogLine> FollowAsync(
        ServerLogsFollowOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = options;
        _ = cancellationToken;
        await Task.FromException(new AppException(AppErrorCodes.InternalError, "server runner disabled"));
        yield break;
    }
}
