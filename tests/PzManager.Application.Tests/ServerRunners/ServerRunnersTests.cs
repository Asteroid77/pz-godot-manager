using PzManager.Application.Errors;
using PzManager.Application.Servers;
using PzManager.Infrastructure.ServerRunners;
using Xunit;

namespace PzManager.Application.Tests.ServerRunners;

public sealed class ServerRunnersTests
{
    [Fact]
    public async Task NullServerRunner_Start_Throws()
    {
        var runner = new NullServerRunner();
        var ex = await Assert.ThrowsAsync<AppException>(() => runner.StartAsync(CancellationToken.None));
        Assert.Equal(AppErrorCodes.InternalError, ex.Code);
    }

    [Fact]
    public async Task BatServerRunner_Start_OnNonWindows_Throws()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runner = new BatServerRunner(new BatServerRunnerOptions("C:/pz/StartServer.bat", "data/pz-server.pid", "data/pz-server.log"));
        var ex = await Assert.ThrowsAsync<AppException>(() => runner.StartAsync(CancellationToken.None));
        Assert.Equal(AppErrorCodes.InternalError, ex.Code);
    }

    [Fact]
    public async Task BatServerRunner_Status_OnNonWindows_ReturnsUnknown()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runner = new BatServerRunner(new BatServerRunnerOptions("C:/pz/StartServer.bat", "data/pz-server.pid", "data/pz-server.log"));
        var status = await runner.GetStatusAsync(CancellationToken.None);
        Assert.Equal("Unknown", status.State.ToString());
    }

    [Fact]
    public async Task NullServerRunner_Tail_Throws()
    {
        var runner = new NullServerRunner();
        var ex = await Assert.ThrowsAsync<AppException>(() => runner.TailAsync(10, CancellationToken.None));
        Assert.Equal(AppErrorCodes.InternalError, ex.Code);
    }

    [Fact]
    public async Task NullServerRunner_Follow_Throws()
    {
        var runner = new NullServerRunner();
        var ex = await Assert.ThrowsAsync<AppException>(async () =>
        {
            await foreach (var _ in runner.FollowAsync(new ServerLogsFollowOptions(10), CancellationToken.None))
            {
                // never yields
            }
        });
        Assert.Equal(AppErrorCodes.InternalError, ex.Code);
    }

    [Fact]
    public async Task BatServerRunner_Tail_OnNonWindows_Throws()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runner = new BatServerRunner(new BatServerRunnerOptions("C:/pz/StartServer.bat", "data/pz-server.pid", "data/pz-server.log"));
        var ex = await Assert.ThrowsAsync<AppException>(() => runner.TailAsync(10, CancellationToken.None));
        Assert.Equal(AppErrorCodes.InternalError, ex.Code);
    }

    [Fact]
    public async Task BatServerRunner_Follow_OnNonWindows_Throws()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runner = new BatServerRunner(new BatServerRunnerOptions("C:/pz/StartServer.bat", "data/pz-server.pid", "data/pz-server.log"));
        var ex = await Assert.ThrowsAsync<AppException>(async () =>
        {
            await foreach (var _ in runner.FollowAsync(new ServerLogsFollowOptions(10), CancellationToken.None))
            {
                // never yields
            }
        });
        Assert.Equal(AppErrorCodes.InternalError, ex.Code);
    }
}
