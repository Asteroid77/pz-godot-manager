using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using PzManager.Application.Abstractions;
using PzManager.Application.Errors;
using PzManager.Application.Servers;
using PzManager.Infrastructure.Process;

namespace PzManager.Infrastructure.ServerRunners;

public sealed class DockerComposeServerRunner : IServerRunner, IServerLogs
{
    private readonly DockerComposeServerRunnerOptions _options;

    public DockerComposeServerRunner(DockerComposeServerRunnerOptions options)
    {
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return RunComposeAsync(
            ["up", "-d", _options.Service],
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return RunComposeAsync(
            ["stop", _options.Service],
            cancellationToken);
    }

    public Task RestartAsync(CancellationToken cancellationToken)
    {
        return RunComposeAsync(
            ["restart", _options.Service],
            cancellationToken);
    }

    public async Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var running = await ComposePsIdsAsync(onlyRunning: true, cancellationToken);
        if (running.Length > 0)
        {
            return new ServerStatus(ServerState.Running, $"container_id={running[0]}");
        }

        var any = await ComposePsIdsAsync(onlyRunning: false, cancellationToken);
        if (any.Length > 0)
        {
            return new ServerStatus(ServerState.Stopped, $"container_id={any[0]}");
        }

        return new ServerStatus(ServerState.Unknown, "container not created");
    }

    public async Task<IReadOnlyList<string>> TailAsync(int maxLines, CancellationToken cancellationToken)
    {
        maxLines = maxLines <= 0 ? 200 : maxLines;
        if (maxLines > 2000)
        {
            maxLines = 2000;
        }

        var args = ComposeArgs(["logs", "--no-color", "--tail", maxLines.ToString(), _options.Service]);
        var result = await CommandRunner.RunAsync("docker", args, workingDirectory: _options.ProjectDirectory, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new AppException(AppErrorCodes.InternalError, "docker compose logs failed", result.StdErr.Trim());
        }

        return result.StdOut
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
    }

    public async IAsyncEnumerable<ServerLogLine> FollowAsync(
        ServerLogsFollowOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (options.Offset is not null)
        {
            throw new AppException(AppErrorCodes.BadRequest, "offset not supported for docker logs");
        }

        var tailLines = options.TailLines <= 0 ? 200 : options.TailLines;
        if (tailLines > 2000)
        {
            tailLines = 2000;
        }

        var composeArgs = new List<string>
        {
            "logs",
            "--no-color",
            "--follow",
            "--tail",
            tailLines.ToString(),
            "--timestamps",
        };

        if (!string.IsNullOrWhiteSpace(options.Since))
        {
            if (options.Since.Length > 128)
            {
                throw new AppException(AppErrorCodes.BadRequest, "since too long");
            }

            composeArgs.Add("--since");
            composeArgs.Add(options.Since);
        }

        composeArgs.Add(_options.Service);

        var args = ComposeArgs(composeArgs);

        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            WorkingDirectory = _options.ProjectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new AppException(AppErrorCodes.InternalError, "failed to start docker compose logs --follow");
        }

        using var _ = cancellationToken.Register(() => TryKill(process));
        var stderrTask = process.StandardError.ReadToEndAsync();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            yield return new ServerLogLine(line, TimestampUtc: ExtractTimestampUtc(line), Offset: null);
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // expected: best-effort stop on cancellation
        }

        TryKill(process);

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch
        {
            // Best-effort.
        }

        var stderr = await stderrTask;
        if (process.ExitCode != 0 && !cancellationToken.IsCancellationRequested)
        {
            throw new AppException(AppErrorCodes.InternalError, "docker compose logs --follow failed", stderr.Trim());
        }
    }

    private async Task<string[]> ComposePsIdsAsync(bool onlyRunning, CancellationToken cancellationToken)
    {
        var args = ComposeArgs(["ps", "-a", "-q", _options.Service]);
        if (onlyRunning)
        {
            args.InsertRange(args.Count - 1, ["--status", "running"]);
        }

        var result = await CommandRunner.RunAsync("docker", args, workingDirectory: _options.ProjectDirectory, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new AppException(AppErrorCodes.InternalError, "docker compose ps failed", result.StdErr.Trim());
        }

        return result.StdOut
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task RunComposeAsync(IReadOnlyList<string> composeArgs, CancellationToken cancellationToken)
    {
        var args = ComposeArgs(composeArgs);
        var result = await CommandRunner.RunAsync("docker", args, workingDirectory: _options.ProjectDirectory, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new AppException(AppErrorCodes.InternalError, "docker compose command failed", result.StdErr.Trim());
        }
    }

    private List<string> ComposeArgs(IReadOnlyList<string> composeArgs)
    {
        var args = new List<string>
        {
            "compose",
            "--project-directory",
            _options.ProjectDirectory,
            "-f",
            _options.ComposeFile,
        };

        args.AddRange(composeArgs);
        return args;
    }

    private static void TryKill(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private static string? ExtractTimestampUtc(string line)
    {
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmed = token.Trim('|');
            if (!trimmed.Contains('T', StringComparison.Ordinal))
            {
                continue;
            }

            if (!DateTimeOffset.TryParse(
                    trimmed,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var timestamp))
            {
                continue;
            }

            return timestamp.ToUniversalTime().ToString("O");
        }

        return null;
    }
}
