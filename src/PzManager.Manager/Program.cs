using PzManager.Application.Abstractions;
using PzManager.Application.Services;
using PzManager.Domain.Security;
using PzManager.Infrastructure.Audit;
using PzManager.Infrastructure.Crypto;
using PzManager.Infrastructure.Persistence;
using PzManager.Infrastructure.ServerRunners;
using PzManager.Infrastructure.Time;
using PzManager.Manager.Ws;
using PzManager.Transport.Protocol;

string? Env(string key) => Environment.GetEnvironmentVariable(key);

var bind = Environment.GetEnvironmentVariable("PZ_MANAGER_BIND") ?? "127.0.0.1";
var port = int.TryParse(Environment.GetEnvironmentVariable("PZ_MANAGER_PORT"), out var parsedPort) ? parsedPort : 27100;
var dataDir = Environment.GetEnvironmentVariable("PZ_MANAGER_DATA_DIR") ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
var bootstrapTtlSeconds =
    int.TryParse(Environment.GetEnvironmentVariable("PZ_MANAGER_BOOTSTRAP_TTL_SECONDS"), out var parsedBootstrapTtl)
        ? parsedBootstrapTtl
        : 1800;

var runnerMode = (Env("PZ_GAME_RUNNER_MODE") ?? "none").Trim();
var dockerProjectDir = Env("PZ_DOCKER_PROJECT_DIR");
var dockerComposeFile = Env("PZ_DOCKER_COMPOSE_FILE");
var dockerService = Env("PZ_DOCKER_SERVICE") ?? "pz-server";
var batPath = Env("PZ_BAT_PATH");
var batLogFile = Env("PZ_BAT_LOG_FILE");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{bind}:{port}");

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IRandomBytesGenerator, CryptoRandomBytesGenerator>();
builder.Services.AddSingleton<ISignatureVerifier, BouncyCastleEd25519SignatureVerifier>();
builder.Services.AddSingleton<IAuditLogWriter>(_ => new JsonlFileAuditLogWriter(Path.Combine(dataDir, "audit.jsonl")));

builder.Services.AddSingleton<IServerRunner>(_ =>
{
    if (string.Equals(runnerMode, "docker-compose", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(dockerProjectDir))
        {
            throw new InvalidOperationException("PZ_DOCKER_PROJECT_DIR required for docker-compose runner");
        }

        var composeFile = string.IsNullOrWhiteSpace(dockerComposeFile)
            ? Path.Combine(dockerProjectDir, "docker-compose.yml")
            : dockerComposeFile;

        return new DockerComposeServerRunner(new DockerComposeServerRunnerOptions(dockerProjectDir, composeFile, dockerService));
    }

    if (string.Equals(runnerMode, "bat", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(batPath))
        {
            throw new InvalidOperationException("PZ_BAT_PATH required for bat runner");
        }

        return new BatServerRunner(
            new BatServerRunnerOptions(
                batPath,
                Path.Combine(dataDir, "pz-server.pid"),
                batLogFile ?? Path.Combine(dataDir, "pz-server.log")));
    }

    return new NullServerRunner();
});

builder.Services.AddSingleton<IServerLogs>(_ =>
{
    if (string.Equals(runnerMode, "docker-compose", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(dockerProjectDir))
        {
            throw new InvalidOperationException("PZ_DOCKER_PROJECT_DIR required for docker-compose runner");
        }

        var composeFile = string.IsNullOrWhiteSpace(dockerComposeFile)
            ? Path.Combine(dockerProjectDir, "docker-compose.yml")
            : dockerComposeFile;

        return new DockerComposeServerRunner(new DockerComposeServerRunnerOptions(dockerProjectDir, composeFile, dockerService));
    }

    if (string.Equals(runnerMode, "bat", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(batPath))
        {
            throw new InvalidOperationException("PZ_BAT_PATH required for bat runner");
        }

        return new BatServerRunner(
            new BatServerRunnerOptions(
                batPath,
                Path.Combine(dataDir, "pz-server.pid"),
                batLogFile ?? Path.Combine(dataDir, "pz-server.log")));
    }

    return new NullServerRunner();
});

builder.Services.AddSingleton<JsonFileManagerStore>(_ => new JsonFileManagerStore(dataDir));
builder.Services.AddSingleton<IDeviceRepository>(sp => sp.GetRequiredService<JsonFileManagerStore>());
builder.Services.AddSingleton<IPairingInvitationRepository>(sp => sp.GetRequiredService<JsonFileManagerStore>());

builder.Services.AddSingleton<PairingCodeService>();
builder.Services.AddSingleton<DeviceAuthService>();
builder.Services.AddSingleton<DeviceAdminService>();
builder.Services.AddSingleton<ManagerWsHandler>();

var app = builder.Build();

static string ManagerWsUrl(string bind, int port)
{
    var host = bind;
    if (string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "::", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "[::]", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "*", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "+", StringComparison.OrdinalIgnoreCase))
    {
        host = "127.0.0.1";
    }

    return new UriBuilder("ws", host, port, "/ws").Uri.ToString();
}

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20),
});

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var handler = context.RequestServices.GetRequiredService<ManagerWsHandler>();
    await handler.HandleAsync(socket, context.RequestAborted);
});

{
    using var scope = app.Services.CreateScope();
    var devices = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
    var pairings = scope.ServiceProvider.GetRequiredService<PairingCodeService>();

    var current = await devices.ListAsync(CancellationToken.None);
    var hasActiveAdmin = current.Any(d => d.Role == Role.Admin && !d.Revoked);
    if (current.Count == 0 || !hasActiveAdmin)
    {
        var invitation = await pairings.GetOrCreateBootstrapAdminAsync(TimeSpan.FromSeconds(bootstrapTtlSeconds), CancellationToken.None);
        app.Logger.LogWarning(
            "Bootstrap pairing code (role=admin, expires={ExpiresAt:o}): {Code}",
            invitation.ExpiresAtUtc,
            invitation.Code.ToDisplayString());

        var bundle = PairingBundleCodec.Encode(
            new PairingBundleV1(
                ManagerUrl: ManagerWsUrl(bind, port),
                PairingCode: invitation.Code.ToDisplayString(),
                Role: invitation.Role.ToString().ToLowerInvariant(),
                ExpiresAtUtc: invitation.ExpiresAtUtc));
        app.Logger.LogWarning("Bootstrap pairing bundle (PZMB1, edit managerUrl if needed): {Bundle}", bundle);
    }
}

app.Run();
