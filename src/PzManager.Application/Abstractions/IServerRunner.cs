using PzManager.Application.Servers;

namespace PzManager.Application.Abstractions;

public interface IServerRunner
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task RestartAsync(CancellationToken cancellationToken);

    Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken);
}

