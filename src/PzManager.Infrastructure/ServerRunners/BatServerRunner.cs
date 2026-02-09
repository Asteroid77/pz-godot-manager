using System.Diagnostics;
using PzManager.Application.Abstractions;
using PzManager.Application.Errors;
using PzManager.Application.Servers;
using PzManager.Infrastructure.Logs;

namespace PzManager.Infrastructure.ServerRunners;

public sealed class BatServerRunner : IServerRunner, IServerLogs
{
    private readonly BatServerRunnerOptions _options;

    public BatServerRunner(BatServerRunnerOptions options)
    {
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new AppException(AppErrorCodes.InternalError, "bat runner only supported on Windows");
        }

        if (_options.ScriptPath.Contains('"') || _options.LogFile.Contains('"'))
        {
            throw new AppException(AppErrorCodes.InternalError, "paths must not contain quotes");
        }

        var pidDir = Path.GetDirectoryName(_options.PidFile);
        if (!string.IsNullOrWhiteSpace(pidDir))
        {
            Directory.CreateDirectory(pidDir);
        }

        var logDir = Path.GetDirectoryName(_options.LogFile);
        if (!string.IsNullOrWhiteSpace(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        try
        {
            if (!File.Exists(_options.LogFile))
            {
                File.WriteAllText(_options.LogFile, "");
            }
        }
        catch (Exception ex)
        {
            throw new AppException(AppErrorCodes.InternalError, "failed to initialize log file", ex.Message);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = Path.GetDirectoryName(_options.ScriptPath) ?? "",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add($"\"{_options.ScriptPath}\" >> \"{_options.LogFile}\" 2>&1");

        try
        {
            var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                throw new AppException(AppErrorCodes.InternalError, "failed to start bat script");
            }

            File.WriteAllText(_options.PidFile, process.Id.ToString());
        }
        catch (AppException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AppException(AppErrorCodes.InternalError, "failed to start bat script", ex.Message);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new AppException(AppErrorCodes.InternalError, "bat runner only supported on Windows");
        }

        if (!File.Exists(_options.PidFile))
        {
            return Task.CompletedTask;
        }

        var raw = File.ReadAllText(_options.PidFile).Trim();
        if (!int.TryParse(raw, out var pid))
        {
            return Task.CompletedTask;
        }

        try
        {
            var process = System.Diagnostics.Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }

        try
        {
            File.Delete(_options.PidFile);
        }
        catch
        {
            // ignore
        }

        return Task.CompletedTask;
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken);
        await StartAsync(cancellationToken);
    }

    public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new ServerStatus(ServerState.Unknown, "bat runner only supported on Windows"));
        }

        if (!File.Exists(_options.PidFile))
        {
            return Task.FromResult(new ServerStatus(ServerState.Stopped, "pid file missing"));
        }

        var raw = File.ReadAllText(_options.PidFile).Trim();
        if (!int.TryParse(raw, out var pid))
        {
            return Task.FromResult(new ServerStatus(ServerState.Error, "invalid pid file"));
        }

        try
        {
            _ = System.Diagnostics.Process.GetProcessById(pid);
            return Task.FromResult(new ServerStatus(ServerState.Running, $"pid={pid}"));
        }
        catch
        {
            return Task.FromResult(new ServerStatus(ServerState.Stopped, $"pid_not_found={pid}"));
        }
    }

    public Task<IReadOnlyList<string>> TailAsync(int maxLines, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new AppException(AppErrorCodes.InternalError, "bat runner only supported on Windows");
        }

        _ = cancellationToken;

        try
        {
            return Task.FromResult(FileLogReader.Tail(_options.LogFile, maxLines));
        }
        catch (FileNotFoundException)
        {
            throw new AppException(AppErrorCodes.InternalError, "bat log file missing");
        }
        catch (Exception ex)
        {
            throw new AppException(AppErrorCodes.InternalError, "bat log tail failed", ex.Message);
        }
    }

    public IAsyncEnumerable<ServerLogLine> FollowAsync(
        ServerLogsFollowOptions options,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new AppException(AppErrorCodes.InternalError, "bat runner only supported on Windows");
        }

        if (!File.Exists(_options.LogFile))
        {
            throw new AppException(AppErrorCodes.InternalError, "bat log file missing");
        }

        var offset = options.Offset;
        if (offset is < 0)
        {
            offset = 0;
        }

        var startOffset = offset ?? FileLogReader.FindTailStartOffset(_options.LogFile, options.TailLines);
        return FileLogReader.FollowAsync(_options.LogFile, startOffset, cancellationToken);
    }
}
