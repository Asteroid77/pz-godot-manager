using System.Text.Json;
using System.Text.Json.Serialization;

namespace PzManager.Client.Ws;

internal static class WsJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

