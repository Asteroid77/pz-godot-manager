using System.Text.Json;
using System.Text.Json.Serialization;

namespace PzManager.Transport.Protocol;

public static class PairingBundleCodec
{
    public const string Prefix = "PZMB1:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Encode(PairingBundleV1 bundle)
    {
        if (bundle is null)
        {
            throw new ArgumentNullException(nameof(bundle));
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(bundle, JsonOptions);
        return Prefix + Base64UrlEncode(json);
    }

    public static bool TryDecode(string? raw, out PairingBundleV1? bundle, out string? error)
    {
        bundle = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "empty bundle";
            return false;
        }

        try
        {
            var s = raw.Trim();
            if (s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[Prefix.Length..];
            }

            if (string.IsNullOrWhiteSpace(s))
            {
                error = "empty payload";
                return false;
            }

            var json = Base64UrlDecode(s);
            bundle = JsonSerializer.Deserialize<PairingBundleV1>(json, JsonOptions);
            if (bundle is null)
            {
                error = "invalid json";
                return false;
            }

            if (string.IsNullOrWhiteSpace(bundle.ManagerUrl))
            {
                error = "missing managerUrl";
                return false;
            }

            if (string.IsNullOrWhiteSpace(bundle.PairingCode))
            {
                error = "missing pairingCode";
                return false;
            }

            if (string.IsNullOrWhiteSpace(bundle.Role))
            {
                error = "missing role";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> data)
    {
        var s = Convert.ToBase64String(data);
        return s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string data)
    {
        var s = data.Replace('-', '+').Replace('_', '/');
        var mod = s.Length % 4;
        if (mod is 2)
        {
            s += "==";
        }
        else if (mod is 3)
        {
            s += "=";
        }
        else if (mod is not 0)
        {
            throw new FormatException("invalid base64url length");
        }

        return Convert.FromBase64String(s);
    }
}

