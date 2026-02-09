using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PzManager.Transport.Protocol;

namespace PzManager.Manager.Ws;

internal static class WebSocketJson
{
    private const int MaxMessageBytes = 64 * 1024;

    internal static async Task<WsEnvelope?> ReceiveEnvelopeAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var json = await ReceiveTextAsync(socket, cancellationToken);
        if (json is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<WsEnvelope>(json, WsJson.Options);
    }

    internal static Task SendAsync<TPayload>(WebSocket socket, WsEnvelope<TPayload> envelope, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(envelope, WsJson.Options);
        return SendTextAsync(socket, json, cancellationToken);
    }

    internal static Task SendErrorAsync(
        WebSocket socket,
        string code,
        string message,
        string? details,
        string? id,
        CancellationToken cancellationToken)
    {
        var payload = new ErrorV1(code, message, details);
        return SendAsync(socket, new WsEnvelope<ErrorV1>(WsMessageTypes.Error, payload, id), cancellationToken);
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            using var ms = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    return null;
                }

                ms.Write(buffer, 0, result.Count);
                if (ms.Length > MaxMessageBytes)
                {
                    return null;
                }

                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Task SendTextAsync(WebSocket socket, string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }
}

