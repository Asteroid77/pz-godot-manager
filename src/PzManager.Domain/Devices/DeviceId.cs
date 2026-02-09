using System.Security.Cryptography;

namespace PzManager.Domain.Devices;

public readonly record struct DeviceId(string Value)
{
    public static DeviceId New() => new(Guid.NewGuid().ToString("N"));

    public static DeviceId FromEd25519PublicKey(ReadOnlySpan<byte> publicKeyBytes)
    {
        if (publicKeyBytes.Length != 32)
        {
            throw new ArgumentException("ed25519 public key must be 32 bytes", nameof(publicKeyBytes));
        }

        var hash = SHA256.HashData(publicKeyBytes);
        return new DeviceId(Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant());
    }

    public override string ToString() => Value;
}
