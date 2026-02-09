namespace PzManager.Infrastructure.Process;

public sealed record CommandResult(
    int ExitCode,
    string StdOut,
    string StdErr);

