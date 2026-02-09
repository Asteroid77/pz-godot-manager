using System.Net.WebSockets;
using System.Text.Json;
using PzManager.Application.Abstractions;
using PzManager.Application.Audit;
using PzManager.Application.Errors;
using PzManager.Application.Servers;
using PzManager.Application.Services;
using PzManager.Domain.Devices;
using PzManager.Domain.Security;
using PzManager.Transport.Protocol;

namespace PzManager.Manager.Ws;

internal sealed class ManagerWsHandler
{
    private const int MaxAuditSecretReveal = 4;

    private readonly IClock _clock;
    private readonly IRandomBytesGenerator _random;
    private readonly DeviceAuthService _auth;
    private readonly PairingCodeService _pairing;
    private readonly DeviceAdminService _deviceAdmin;
    private readonly IServerRunner _serverRunner;
    private readonly IServerLogs _serverLogs;
    private readonly IAuditLogWriter _audit;
    private readonly ILogger<ManagerWsHandler> _logger;

    public ManagerWsHandler(
        ILogger<ManagerWsHandler> logger,
        IClock clock,
        IRandomBytesGenerator random,
        DeviceAuthService auth,
        PairingCodeService pairing,
        DeviceAdminService deviceAdmin,
        IServerRunner serverRunner,
        IServerLogs serverLogs,
        IAuditLogWriter audit)
    {
        _logger = logger;
        _clock = clock;
        _random = random;
        _auth = auth;
        _pairing = pairing;
        _deviceAdmin = deviceAdmin;
        _serverRunner = serverRunner;
        _serverLogs = serverLogs;
        _audit = audit;
    }

    public async Task HandleAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var sendLock = new SemaphoreSlim(1, 1);

        Task SendAsync<TPayload>(WsEnvelope<TPayload> envelope, CancellationToken ct) =>
            SendWithLockAsync(() => WebSocketJson.SendAsync(socket, envelope, ct), ct);

        Task SendErrorAsync(string code, string message, string? details, string? id, CancellationToken ct) =>
            SendWithLockAsync(() => WebSocketJson.SendErrorAsync(socket, code, message, details, id, ct), ct);

	        async Task SendWithLockAsync(Func<Task> send, CancellationToken ct)
	        {
	            await sendLock!.WaitAsync(ct);
	            try
	            {
	                await send();
	            }
	            finally
	            {
	                sendLock.Release();
	            }
	        }

        var nonce = _random.GetBytes(32);
        await SendAsync(
            new WsEnvelope<ServerHelloV1>(
                WsMessageTypes.Hello,
                new ServerHelloV1(Protocol: 1, Nonce: Convert.ToBase64String(nonce), ServerTimeUtc: _clock.UtcNow)),
            cancellationToken);

        var first = await WebSocketJson.ReceiveEnvelopeAsync(socket, cancellationToken);
        if (first is null)
        {
            return;
        }

        if (!string.Equals(first.Type, WsMessageTypes.Auth, StringComparison.OrdinalIgnoreCase))
        {
            await SendErrorAsync(ErrorCodes.Unauthorized, "auth required", null, first.Id, cancellationToken);
            return;
        }

        var authReq = DeserializePayload<AuthRequestV1>(first, out var authErr);
        if (authReq is null)
        {
            await SendErrorAsync(ErrorCodes.BadRequest, "invalid auth payload", authErr, first.Id, cancellationToken);
            return;
        }

        byte[] publicKeyBytes;
        byte[] signatureBytes;
        try
        {
            publicKeyBytes = Convert.FromBase64String(authReq.PublicKey);
            signatureBytes = Convert.FromBase64String(authReq.Signature);
        }
        catch (FormatException)
        {
            await SendErrorAsync(ErrorCodes.BadRequest, "invalid base64", null, first.Id, cancellationToken);
            return;
        }

        AuthResult auth;
        try
        {
            auth = await _auth.AuthenticateAsync(
                new AuthenticateDeviceCommand(
                    PublicKey: publicKeyBytes,
                    Signature: signatureBytes,
                    Nonce: nonce,
                    PairingCode: authReq.PairingCode,
                    DeviceName: authReq.DeviceName),
                cancellationToken);
        }
        catch (AppException ex)
        {
            await SendErrorAsync(ex.Code, ex.Message, ex.Details, first.Id, cancellationToken);
            return;
        }

        var caps = new HashSet<string>(auth.Capabilities, StringComparer.OrdinalIgnoreCase);
        await SendAsync(
            new WsEnvelope<AuthOkV1>(
                WsMessageTypes.AuthOk,
                new AuthOkV1(
                    DeviceId: auth.DeviceId.Value,
                    Role: auth.Role.ToString().ToLowerInvariant(),
                    Capabilities: auth.Capabilities,
                    IsNewDevice: auth.IsNewDevice),
                first.Id),
            cancellationToken);

        _logger.LogInformation("WS authenticated: device={DeviceId} role={Role}", auth.DeviceId.Value, auth.Role);
        await TryAuditAsync(
            new AuditEvent(
                TimeUtc: _clock.UtcNow,
                ActorDeviceId: auth.DeviceId.Value,
                ActorRole: auth.Role.ToString().ToLowerInvariant(),
                Action: auth.IsNewDevice ? "device.register" : "ws.auth",
                TargetId: auth.DeviceId.Value,
                Data: authReq.DeviceName is null ? null : new Dictionary<string, string> { ["deviceName"] = authReq.DeviceName }),
            cancellationToken);

        CancellationTokenSource? followCts = null;
        Task? followTask = null;
        string? followId = null;

        void ClearFollowIfCompleted()
        {
            if (followTask is null || !followTask.IsCompleted)
            {
                return;
            }

            followTask = null;
            followCts?.Dispose();
            followCts = null;
            followId = null;
        }

        async Task FlushFollowAsync(List<string> buffer, string currentFollowId, long? nextOffset, string? nextSince, CancellationToken ct)
        {
            if (buffer.Count == 0)
            {
                return;
            }

            await SendAsync(
                new WsEnvelope<LogsFollowOkV1>(
                    WsMessageTypes.LogsFollowOk,
                    new LogsFollowOkV1(
                        currentFollowId,
                        buffer.ToArray(),
                        IsEnd: false,
                        Reason: null,
                        NextOffset: nextOffset,
                        NextSince: nextSince)),
                ct);
        }

        async Task RunFollowAsync(string currentFollowId, ServerLogsFollowOptions followOptions, int batchLines, CancellationToken ct)
        {
            try
            {
                var buffer = new List<string>(batchLines);
                var flushInterval = TimeSpan.FromMilliseconds(250);
                var nextFlushAt = _clock.UtcNow;
                long? nextOffset = null;
                string? nextSince = null;

                await foreach (var line in _serverLogs.FollowAsync(followOptions, ct))
                {
                    buffer.Add(line.Text);
                    nextOffset = line.Offset ?? nextOffset;
                    nextSince = line.TimestampUtc ?? nextSince;
                    var now = _clock.UtcNow;
                    if (buffer.Count >= batchLines || now >= nextFlushAt)
                    {
                        await FlushFollowAsync(buffer, currentFollowId, nextOffset, nextSince, ct);
                        buffer.Clear();
                        nextFlushAt = now + flushInterval;
                    }
                }

                await FlushFollowAsync(buffer, currentFollowId, nextOffset, nextSince, ct);

                if (!ct.IsCancellationRequested)
                {
                    await SendAsync(
                        new WsEnvelope<LogsFollowOkV1>(
                            WsMessageTypes.LogsFollowOk,
                            new LogsFollowOkV1(
                                currentFollowId,
                                Array.Empty<string>(),
                                IsEnd: true,
                                Reason: "eof",
                                NextOffset: nextOffset,
                                NextSince: nextSince)),
                        ct);
                }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            catch (AppException ex)
            {
                _logger.LogWarning(ex, "logs.follow failed");
                if (!ct.IsCancellationRequested)
                {
                    await SendErrorAsync(ex.Code, ex.Message, ex.Details, null, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "logs.follow crashed");
                if (!ct.IsCancellationRequested)
                {
                    await SendErrorAsync(ErrorCodes.InternalError, "logs.follow crashed", ex.Message, null, ct);
                }
            }
        }

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var env = await WebSocketJson.ReceiveEnvelopeAsync(socket, cancellationToken);
                if (env is null)
                {
                    return;
                }

                ClearFollowIfCompleted();

                if (string.Equals(env.Type, WsMessageTypes.Ping, StringComparison.OrdinalIgnoreCase))
                {
                    var ping = DeserializePayload<PingV1>(env, out _) ?? new PingV1(null);
                    await SendAsync(new WsEnvelope<PongV1>(WsMessageTypes.Pong, new PongV1(ping.Message), env.Id), cancellationToken);
                    continue;
                }

            if (string.Equals(env.Type, WsMessageTypes.WhoAmI, StringComparison.OrdinalIgnoreCase))
            {
                await SendAsync(
                    new WsEnvelope<WhoAmIOkV1>(
                        WsMessageTypes.WhoAmIOk,
                        new WhoAmIOkV1(auth.DeviceId.Value, auth.Role.ToString().ToLowerInvariant(), DeviceName: authReq.DeviceName),
                        env.Id),
                    cancellationToken);
                continue;
            }

            if (string.Equals(env.Type, WsMessageTypes.PairingCreate, StringComparison.OrdinalIgnoreCase))
            {
                if (!caps.Contains(Capabilities.PairingCreate))
                {
                    await SendErrorAsync(ErrorCodes.Forbidden, "missing capability: pairing_create", null, env.Id, cancellationToken);
                    continue;
                }

                var req = DeserializePayload<PairingCreateRequestV1>(env, out var err);
                if (req is null)
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "invalid pairing.create payload", err, env.Id, cancellationToken);
                    continue;
                }

                if (!Enum.TryParse<Role>(req.Role, ignoreCase: true, out var role))
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "invalid role", req.Role, env.Id, cancellationToken);
                    continue;
                }

                try
                {
                    var invitation = await _pairing.CreateAsync(role, TimeSpan.FromSeconds(req.TtlSeconds), cancellationToken);
                    await SendAsync(
                        new WsEnvelope<PairingCreateOkV1>(
                            WsMessageTypes.PairingCreateOk,
                            new PairingCreateOkV1(invitation.Code.ToDisplayString(), invitation.ExpiresAtUtc),
                            env.Id),
                        cancellationToken);

                    await TryAuditAsync(
                        new AuditEvent(
                            TimeUtc: _clock.UtcNow,
                            ActorDeviceId: auth.DeviceId.Value,
                            ActorRole: auth.Role.ToString().ToLowerInvariant(),
                            Action: "pairing.create",
                            TargetId: null,
                            Data: new Dictionary<string, string>
                            {
                                ["role"] = role.ToString().ToLowerInvariant(),
                                ["ttlSeconds"] = req.TtlSeconds.ToString(),
                                ["expiresAtUtc"] = invitation.ExpiresAtUtc.ToString("O"),
                                ["codeMasked"] = MaskSecret(invitation.Code.Value),
                            }),
                        cancellationToken);
                }
                catch (AppException ex)
                {
                    await SendErrorAsync(ex.Code, ex.Message, ex.Details, env.Id, cancellationToken);
                }

                continue;
            }

            if (string.Equals(env.Type, WsMessageTypes.DevicesList, StringComparison.OrdinalIgnoreCase))
            {
                if (!caps.Contains(Capabilities.DevicesRead))
                {
                    await SendErrorAsync(ErrorCodes.Forbidden, "missing capability: devices_read", null, env.Id, cancellationToken);
                    continue;
                }

                var devices = await _deviceAdmin.ListAsync(cancellationToken);
                var payload = new DevicesListOkV1(devices.Select(ToDeviceInfo).ToArray());

                await SendAsync(
                    new WsEnvelope<DevicesListOkV1>(WsMessageTypes.DevicesListOk, payload, env.Id),
                    cancellationToken);
                continue;
            }

            if (string.Equals(env.Type, WsMessageTypes.DevicesRevoke, StringComparison.OrdinalIgnoreCase))
            {
                if (!caps.Contains(Capabilities.DevicesRevoke))
                {
                    await SendErrorAsync(ErrorCodes.Forbidden, "missing capability: devices_revoke", null, env.Id, cancellationToken);
                    continue;
                }

                var req = DeserializePayload<DeviceRevokeRequestV1>(env, out var err);
                if (req is null)
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "invalid devices.revoke payload", err, env.Id, cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(req.DeviceId))
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "deviceId required", null, env.Id, cancellationToken);
                    continue;
                }

                try
                {
                    var updated = await _deviceAdmin.RevokeAsync(new DeviceId(req.DeviceId), req.Note, cancellationToken);
                    await SendAsync(
                        new WsEnvelope<DeviceRevokeOkV1>(
                            WsMessageTypes.DevicesRevokeOk,
                            new DeviceRevokeOkV1(updated.Id.Value, updated.Revoked),
                            env.Id),
                        cancellationToken);

                    await TryAuditAsync(
                        new AuditEvent(
                            TimeUtc: _clock.UtcNow,
                            ActorDeviceId: auth.DeviceId.Value,
                            ActorRole: auth.Role.ToString().ToLowerInvariant(),
                            Action: "devices.revoke",
                            TargetId: updated.Id.Value,
                            Data: req.Note is null ? null : new Dictionary<string, string> { ["note"] = req.Note }),
                        cancellationToken);
                }
                catch (AppException ex)
                {
                    await SendErrorAsync(ex.Code, ex.Message, ex.Details, env.Id, cancellationToken);
                }

                continue;
            }

            if (string.Equals(env.Type, WsMessageTypes.DevicesUpdate, StringComparison.OrdinalIgnoreCase))
            {
                if (!caps.Contains(Capabilities.DevicesWrite))
                {
                    await SendErrorAsync(ErrorCodes.Forbidden, "missing capability: devices_write", null, env.Id, cancellationToken);
                    continue;
                }

                var req = DeserializePayload<DeviceUpdateRequestV1>(env, out var err);
                if (req is null)
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "invalid devices.update payload", err, env.Id, cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(req.DeviceId))
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "deviceId required", null, env.Id, cancellationToken);
                    continue;
                }

                Role? role = null;
                if (req.Role is not null)
                {
                    if (!Enum.TryParse<Role>(req.Role, ignoreCase: true, out var parsedRole))
                    {
                        await SendErrorAsync(ErrorCodes.BadRequest, "invalid role", req.Role, env.Id, cancellationToken);
                        continue;
                    }

                    role = parsedRole;
                }

                try
                {
                    var updated = await _deviceAdmin.UpdateAsync(new DeviceId(req.DeviceId), role, req.Note, cancellationToken);
                    await SendAsync(
                        new WsEnvelope<DeviceUpdateOkV1>(
                            WsMessageTypes.DevicesUpdateOk,
                            new DeviceUpdateOkV1(ToDeviceInfo(updated)),
                            env.Id),
                        cancellationToken);

                    await TryAuditAsync(
                        new AuditEvent(
                            TimeUtc: _clock.UtcNow,
                            ActorDeviceId: auth.DeviceId.Value,
                            ActorRole: auth.Role.ToString().ToLowerInvariant(),
                            Action: "devices.update",
                            TargetId: updated.Id.Value,
                            Data: new Dictionary<string, string>
                            {
                                ["role"] = updated.Role.ToString().ToLowerInvariant(),
                                ["note"] = updated.Note ?? "",
                            }),
                        cancellationToken);
                }
                catch (AppException ex)
                {
                    await SendErrorAsync(ex.Code, ex.Message, ex.Details, env.Id, cancellationToken);
                }

                continue;
            }

            if (string.Equals(env.Type, WsMessageTypes.ServerStatus, StringComparison.OrdinalIgnoreCase))
            {
                var status = await _serverRunner.GetStatusAsync(cancellationToken);
                await SendAsync(
                    new WsEnvelope<ServerStatusOkV1>(
                        WsMessageTypes.ServerStatusOk,
                        new ServerStatusOkV1(status.State.ToString().ToLowerInvariant(), status.Details),
                        env.Id),
                    cancellationToken);
                continue;
            }

            if (string.Equals(env.Type, WsMessageTypes.ServerStart, StringComparison.OrdinalIgnoreCase)
                || string.Equals(env.Type, WsMessageTypes.ServerStop, StringComparison.OrdinalIgnoreCase)
                || string.Equals(env.Type, WsMessageTypes.ServerRestart, StringComparison.OrdinalIgnoreCase))
            {
                if (!caps.Contains(Capabilities.ServerRestart))
                {
                    await SendErrorAsync(ErrorCodes.Forbidden, "missing capability: server_restart", null, env.Id, cancellationToken);
                    continue;
                }

                var req = DeserializePayload<ServerControlRequestV1>(env, out _);

                try
                {
                    if (string.Equals(env.Type, WsMessageTypes.ServerStart, StringComparison.OrdinalIgnoreCase))
                    {
                        await _serverRunner.StartAsync(cancellationToken);
                    }
                    else if (string.Equals(env.Type, WsMessageTypes.ServerStop, StringComparison.OrdinalIgnoreCase))
                    {
                        await _serverRunner.StopAsync(cancellationToken);
                    }
                    else
                    {
                        await _serverRunner.RestartAsync(cancellationToken);
                    }

                    var status = await _serverRunner.GetStatusAsync(cancellationToken);
                    var okType =
                        string.Equals(env.Type, WsMessageTypes.ServerStart, StringComparison.OrdinalIgnoreCase)
                            ? WsMessageTypes.ServerStartOk
                            : string.Equals(env.Type, WsMessageTypes.ServerStop, StringComparison.OrdinalIgnoreCase)
                                ? WsMessageTypes.ServerStopOk
                                : WsMessageTypes.ServerRestartOk;

                    await SendAsync(
                        new WsEnvelope<ServerControlOkV1>(
                            okType,
                            new ServerControlOkV1(status.State.ToString().ToLowerInvariant(), status.Details),
                            env.Id),
                        cancellationToken);

                    var action =
                        string.Equals(env.Type, WsMessageTypes.ServerStart, StringComparison.OrdinalIgnoreCase)
                            ? "server.start"
                            : string.Equals(env.Type, WsMessageTypes.ServerStop, StringComparison.OrdinalIgnoreCase)
                                ? "server.stop"
                                : "server.restart";

                    await TryAuditAsync(
                        new AuditEvent(
                            TimeUtc: _clock.UtcNow,
                            ActorDeviceId: auth.DeviceId.Value,
                            ActorRole: auth.Role.ToString().ToLowerInvariant(),
                            Action: action,
                            TargetId: "pz-server",
                            Data: new Dictionary<string, string>
                            {
                                ["reason"] = req?.Reason ?? "",
                                ["state"] = status.State.ToString().ToLowerInvariant(),
                                ["details"] = status.Details ?? "",
                            }),
                        cancellationToken);
                }
                catch (AppException ex)
                {
                    await SendErrorAsync(ex.Code, ex.Message, ex.Details, env.Id, cancellationToken);
                }

                continue;
            }

            if (string.Equals(env.Type, WsMessageTypes.LogsFollow, StringComparison.OrdinalIgnoreCase))
            {
                if (!caps.Contains(Capabilities.LogsRead))
                {
                    await SendErrorAsync(ErrorCodes.Forbidden, "missing capability: logs_read", null, env.Id, cancellationToken);
                    continue;
                }

                var req = DeserializePayload<LogsFollowRequestV1>(env, out _) ?? new LogsFollowRequestV1(200, 50);
                var tailLines = req.TailLines <= 0 ? 200 : req.TailLines;
                if (tailLines > 2000)
                {
                    tailLines = 2000;
                }

                var batchLines = req.BatchLines <= 0 ? 50 : req.BatchLines;
                if (batchLines > 200)
                {
                    batchLines = 200;
                }

                var since = string.IsNullOrWhiteSpace(req.Since) ? null : req.Since.Trim();
                if (since is not null && since.Length > 128)
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "since too long", null, env.Id, cancellationToken);
                    continue;
                }

                var offset = req.Offset;
                if (offset is < 0)
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "offset must be >= 0", null, env.Id, cancellationToken);
                    continue;
                }

                ClearFollowIfCompleted();
                if (followTask is not null)
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "logs follow already running", followId, env.Id, cancellationToken);
                    continue;
                }

                followId = Guid.NewGuid().ToString("N");
                followCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var currentFollowId = followId;
                var followOptions = new ServerLogsFollowOptions(tailLines, since, offset);
                followTask = RunFollowAsync(currentFollowId, followOptions, batchLines, followCts.Token);

                await SendAsync(
                    new WsEnvelope<LogsFollowOkV1>(
                        WsMessageTypes.LogsFollowOk,
                        new LogsFollowOkV1(
                            currentFollowId,
                            Array.Empty<string>(),
                            IsEnd: false,
                            Reason: null,
                            NextOffset: offset,
                            NextSince: since),
                        env.Id),
                    cancellationToken);
                continue;
            }

            if (string.Equals(env.Type, WsMessageTypes.LogsFollowStop, StringComparison.OrdinalIgnoreCase))
            {
                if (!caps.Contains(Capabilities.LogsRead))
                {
                    await SendErrorAsync(ErrorCodes.Forbidden, "missing capability: logs_read", null, env.Id, cancellationToken);
                    continue;
                }

                var req = DeserializePayload<LogsFollowStopRequestV1>(env, out var err);
                if (req is null)
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "invalid logs.follow.stop payload", err, env.Id, cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(req.FollowId))
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "followId required", null, env.Id, cancellationToken);
                    continue;
                }

                ClearFollowIfCompleted();
                if (followTask is null || followCts is null || followId is null)
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "no logs follow running", null, env.Id, cancellationToken);
                    continue;
                }

                if (!string.Equals(req.FollowId, followId, StringComparison.Ordinal))
                {
                    await SendErrorAsync(ErrorCodes.BadRequest, "unknown followId", req.FollowId, env.Id, cancellationToken);
                    continue;
                }

                try
                {
                    followCts.Cancel();
                }
                catch
                {
                    // ignore
                }

                try
                {
                    await followTask;
                }
                catch
                {
                    // ignore
                }

                followTask = null;
                followCts.Dispose();
                followCts = null;
                followId = null;

                await SendAsync(
                    new WsEnvelope<LogsFollowStopOkV1>(
                        WsMessageTypes.LogsFollowStopOk,
                        new LogsFollowStopOkV1(req.FollowId),
                        env.Id),
                    cancellationToken);
                continue;
            }

            if (string.Equals(env.Type, WsMessageTypes.LogsTail, StringComparison.OrdinalIgnoreCase))
            {
                if (!caps.Contains(Capabilities.LogsRead))
                {
                    await SendErrorAsync(ErrorCodes.Forbidden, "missing capability: logs_read", null, env.Id, cancellationToken);
                    continue;
                }

                var req = DeserializePayload<LogsTailRequestV1>(env, out _) ?? new LogsTailRequestV1(200);
                var maxLines = req.MaxLines <= 0 ? 200 : req.MaxLines;
                if (maxLines > 500)
                {
                    maxLines = 500;
                }

                try
                {
                    var lines = await _serverLogs.TailAsync(maxLines, cancellationToken);
                    await SendAsync(
                        new WsEnvelope<LogsTailOkV1>(
                            WsMessageTypes.LogsTailOk,
                            new LogsTailOkV1(lines.ToArray()),
                            env.Id),
                        cancellationToken);
                }
                catch (AppException ex)
                {
                    await SendErrorAsync(ex.Code, ex.Message, ex.Details, env.Id, cancellationToken);
                }

                continue;
            }

            await SendErrorAsync(ErrorCodes.BadRequest, "unknown message type", env.Type, env.Id, cancellationToken);
        }
        }
        finally
        {
            if (followCts is not null)
            {
                try
                {
                    followCts.Cancel();
                }
                catch
                {
                    // ignore
                }
            }

            if (followTask is not null)
            {
                try
                {
                    await followTask;
                }
                catch
                {
                    // ignore
                }
            }

            followCts?.Dispose();
        }
    }

    private static DeviceInfoV1 ToDeviceInfo(Device device)
    {
        return new DeviceInfoV1(
            DeviceId: device.Id.Value,
            Role: device.Role.ToString().ToLowerInvariant(),
            Name: device.Name,
            Note: device.Note,
            Revoked: device.Revoked,
            CreatedAtUtc: device.CreatedAtUtc,
            LastSeenUtc: device.LastSeenUtc);
    }

    private async Task TryAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _audit.WriteAsync(auditEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "audit log write failed: {Action}", auditEvent.Action);
        }
    }

    private static string MaskSecret(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return "****";
        }

        if (secret.Length <= MaxAuditSecretReveal * 2)
        {
            return "****";
        }

        var head = secret[..MaxAuditSecretReveal];
        var tail = secret[^MaxAuditSecretReveal..];
        return head + "****" + tail;
    }

    private static TPayload? DeserializePayload<TPayload>(WsEnvelope envelope, out string? error)
    {
        error = null;
        try
        {
            return envelope.Payload.Deserialize<TPayload>(WsJson.Options);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return default;
        }
    }
}
