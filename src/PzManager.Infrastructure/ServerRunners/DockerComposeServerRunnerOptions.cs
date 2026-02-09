namespace PzManager.Infrastructure.ServerRunners;

public sealed record DockerComposeServerRunnerOptions(
    string ProjectDirectory,
    string ComposeFile,
    string Service);

