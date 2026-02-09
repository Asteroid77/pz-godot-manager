using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using PzManager.Client.Ws;
using PzManager.Transport.Protocol;

namespace PzManager.Client;

public sealed class ManagerWsClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly CancellationTokenSource _cts;
    private readonly Task _receiveLoop;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WsEnvelope>> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Channel<LogsFollowOkV1>> _logFollowers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<LogsFollowOkV1>> _logFollowerBuffers = new(StringComparer.Ordinal);

    private ManagerWsClient(ClientWebSocket socket, AuthOkV1 auth)
    {
        _socket = socket;
        Auth = auth;
        _cts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    public AuthOkV1 Auth { get; }

    public static async Task<ManagerWsClient> ConnectAsync(ManagerWsClientOptions options, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(options.Uri, cancellationToken);

        var helloEnv = await WebSocketJson.ReceiveEnvelopeAsync(socket, cancellationToken);
        if (helloEnv is null || !string.Equals(helloEnv.Type, WsMessageTypes.Hello, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("server did not send hello");
        }

        var hello = DeserializePayload<ServerHelloV1>(helloEnv);
        var nonce = Convert.FromBase64String(hello.Nonce);
        var signature = options.DeviceKey.Sign(nonce);

        var authReq = new AuthRequestV1(
            PublicKey: Convert.ToBase64String(options.DeviceKey.PublicKey),
            Signature: Convert.ToBase64String(signature),
            PairingCode: options.PairingCode,
            DeviceName: options.DeviceName);

        var id = Guid.NewGuid().ToString("N");
        await WebSocketJson.SendAsync(socket, new WsEnvelope<AuthRequestV1>(WsMessageTypes.Auth, authReq, id), cancellationToken);

        var authEnv = await WebSocketJson.ReceiveEnvelopeAsync(socket, cancellationToken);
        if (authEnv is null)
        {
            throw new InvalidOperationException("server closed during auth");
        }

        if (string.Equals(authEnv.Type, WsMessageTypes.Error, StringComparison.OrdinalIgnoreCase))
        {
            throw ManagerWsException.FromError(DeserializePayload<ErrorV1>(authEnv));
        }

        if (!string.Equals(authEnv.Type, WsMessageTypes.AuthOk, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"unexpected auth response: {authEnv.Type}");
        }

        var authOk = DeserializePayload<AuthOkV1>(authEnv);
        return new ManagerWsClient(socket, authOk);
    }

    public async Task<PongV1> PingAsync(string? message, CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.Ping, new PingV1(message), cancellationToken);
        return Expect<PongV1>(env, WsMessageTypes.Pong);
    }

    public async Task<WhoAmIOkV1> WhoAmIAsync(CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.WhoAmI, new EmptyPayload(), cancellationToken);
        return Expect<WhoAmIOkV1>(env, WsMessageTypes.WhoAmIOk);
    }

    public async Task<PairingCreateOkV1> PairingCreateAsync(string role, int ttlSeconds, CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.PairingCreate, new PairingCreateRequestV1(role, ttlSeconds), cancellationToken);
        return Expect<PairingCreateOkV1>(env, WsMessageTypes.PairingCreateOk);
    }

    public async Task<DevicesListOkV1> DevicesListAsync(CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.DevicesList, new EmptyPayload(), cancellationToken);
        return Expect<DevicesListOkV1>(env, WsMessageTypes.DevicesListOk);
    }

    public async Task<DeviceRevokeOkV1> DevicesRevokeAsync(string deviceId, string? note, CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.DevicesRevoke, new DeviceRevokeRequestV1(deviceId, note), cancellationToken);
        return Expect<DeviceRevokeOkV1>(env, WsMessageTypes.DevicesRevokeOk);
    }

    public async Task<DeviceUpdateOkV1> DevicesUpdateAsync(string deviceId, string? role, string? note, CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.DevicesUpdate, new DeviceUpdateRequestV1(deviceId, role, note), cancellationToken);
        return Expect<DeviceUpdateOkV1>(env, WsMessageTypes.DevicesUpdateOk);
    }

    public async Task<ServerStatusOkV1> ServerStatusAsync(CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.ServerStatus, new EmptyPayload(), cancellationToken);
        return Expect<ServerStatusOkV1>(env, WsMessageTypes.ServerStatusOk);
    }

    public async Task<ServerControlOkV1> ServerStartAsync(string? reason, CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.ServerStart, new ServerControlRequestV1(reason), cancellationToken);
        return Expect<ServerControlOkV1>(env, WsMessageTypes.ServerStartOk);
    }

    public async Task<ServerControlOkV1> ServerStopAsync(string? reason, CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.ServerStop, new ServerControlRequestV1(reason), cancellationToken);
        return Expect<ServerControlOkV1>(env, WsMessageTypes.ServerStopOk);
    }

    public async Task<ServerControlOkV1> ServerRestartAsync(string? reason, CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.ServerRestart, new ServerControlRequestV1(reason), cancellationToken);
        return Expect<ServerControlOkV1>(env, WsMessageTypes.ServerRestartOk);
    }

    public async Task<LogsTailOkV1> LogsTailAsync(int maxLines, CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.LogsTail, new LogsTailRequestV1(maxLines), cancellationToken);
        return Expect<LogsTailOkV1>(env, WsMessageTypes.LogsTailOk);
    }

    public async Task<LogsFollowOkV1> LogsFollowStartAsync(LogsFollowRequestV1 request, CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.LogsFollow, request, cancellationToken);
        var ok = Expect<LogsFollowOkV1>(env, WsMessageTypes.LogsFollowOk);

        var ch = Channel.CreateUnbounded<LogsFollowOkV1>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        if (!_logFollowers.TryAdd(ok.FollowId, ch))
        {
            throw new InvalidOperationException("duplicate followId");
        }

        if (_logFollowerBuffers.TryRemove(ok.FollowId, out var buffered))
        {
            while (buffered.TryDequeue(out var msg))
            {
                ch.Writer.TryWrite(msg);
            }
        }

        return ok;
    }

    public IAsyncEnumerable<LogsFollowOkV1> LogsFollowMessages(string followId)
    {
        if (!_logFollowers.TryGetValue(followId, out var ch))
        {
            throw new InvalidOperationException("unknown followId");
        }

        return ch.Reader.ReadAllAsync();
    }

    public async Task<LogsFollowStopOkV1> LogsFollowStopAsync(string followId, CancellationToken cancellationToken)
    {
        var env = await RequestAsync(WsMessageTypes.LogsFollowStop, new LogsFollowStopRequestV1(followId), cancellationToken);
        var ok = Expect<LogsFollowStopOkV1>(env, WsMessageTypes.LogsFollowStopOk);

        if (_logFollowers.TryRemove(followId, out var ch))
        {
            ch.Writer.TryComplete();
        }

        return ok;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _receiveLoop;
        }
        catch
        {
            // ignore
        }

        try
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch
        {
            // ignore
        }

        _socket.Dispose();
        _cts.Dispose();
        _sendLock.Dispose();
    }

    private static TPayload DeserializePayload<TPayload>(WsEnvelope env)
    {
        var payload = env.Payload.Deserialize<TPayload>(WsJson.Options);
        if (payload is null)
        {
            throw new InvalidOperationException("invalid payload");
        }

        return payload;
    }

    private async Task<WsEnvelope> RequestAsync<TPayload>(string type, TPayload payload, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<WsEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, tcs))
        {
            throw new InvalidOperationException("duplicate request id");
        }

        try
        {
            await SendAsync(new WsEnvelope<TPayload>(type, payload, id), cancellationToken);
            return await tcs.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task SendAsync<TPayload>(WsEnvelope<TPayload> env, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await WebSocketJson.SendAsync(_socket, env, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var env = await WebSocketJson.ReceiveEnvelopeAsync(_socket, cancellationToken);
                if (env is null)
                {
                    break;
                }

                if (env.Id is not null && _pending.TryRemove(env.Id, out var tcs))
                {
                    tcs.TrySetResult(env);
                    continue;
                }

                if (string.Equals(env.Type, WsMessageTypes.LogsFollowOk, StringComparison.OrdinalIgnoreCase))
                {
                    var ok = env.Payload.Deserialize<LogsFollowOkV1>(WsJson.Options);
                    if (ok is null)
                    {
                        continue;
                    }

                    if (_logFollowers.TryGetValue(ok.FollowId, out var ch))
                    {
                        ch.Writer.TryWrite(ok);
                        if (ok.IsEnd)
                        {
                            ch.Writer.TryComplete();
                        }

                        continue;
                    }

                    var q = _logFollowerBuffers.GetOrAdd(ok.FollowId, _ => new ConcurrentQueue<LogsFollowOkV1>());
                    q.Enqueue(ok);
                    while (q.Count > 128 && q.TryDequeue(out _))
                    {
                        // keep bounded
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        catch
        {
            // ignore; shutdown below
        }
        finally
        {
            foreach (var kv in _pending)
            {
                kv.Value.TrySetCanceled();
            }

            foreach (var kv in _logFollowers)
            {
                kv.Value.Writer.TryComplete();
            }
        }
    }

    private static TPayload Expect<TPayload>(WsEnvelope env, string expectedType)
    {
        if (string.Equals(env.Type, WsMessageTypes.Error, StringComparison.OrdinalIgnoreCase))
        {
            throw ManagerWsException.FromError(DeserializePayload<ErrorV1>(env));
        }

        if (!string.Equals(env.Type, expectedType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"unexpected response: {env.Type} (expected {expectedType})");
        }

        return DeserializePayload<TPayload>(env);
    }

    private sealed record EmptyPayload();
}
