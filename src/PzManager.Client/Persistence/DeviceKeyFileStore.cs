using System.Text.Json;
using PzManager.Client.Crypto;

namespace PzManager.Client.Persistence;

public static class DeviceKeyFileStore
{
    private sealed record DeviceKeyFileV1(string PublicKey, string PrivateKey, DateTimeOffset CreatedAtUtc);

    public static Ed25519KeyPair LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            return Load(path);
        }

        var kp = Ed25519KeyPair.Generate();
        Save(path, kp);
        return kp;
    }

    public static Ed25519KeyPair Load(string path)
    {
        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<DeviceKeyFileV1>(json);
        if (file is null)
        {
            throw new InvalidOperationException("invalid key file");
        }

        return new Ed25519KeyPair(
            Convert.FromBase64String(file.PublicKey),
            Convert.FromBase64String(file.PrivateKey));
    }

    public static void Save(string path, Ed25519KeyPair keyPair)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var file = new DeviceKeyFileV1(
            PublicKey: Convert.ToBase64String(keyPair.PublicKey),
            PrivateKey: Convert.ToBase64String(keyPair.PrivateKey),
            CreatedAtUtc: DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json + "\n");
    }
}

