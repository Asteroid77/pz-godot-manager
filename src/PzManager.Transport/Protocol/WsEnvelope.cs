using System.Text.Json;

namespace PzManager.Transport.Protocol;

public sealed record WsEnvelope(string Type, JsonElement Payload, string? Id = null);

public sealed record WsEnvelope<TPayload>(string Type, TPayload Payload, string? Id = null);

