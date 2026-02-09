namespace PzManager.Infrastructure.ServerRunners;

public sealed record BatServerRunnerOptions(
    string ScriptPath,
    string PidFile,
    string LogFile);
