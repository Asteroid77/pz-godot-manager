using PzManager.Transport.Protocol;
using Xunit;

namespace PzManager.Client.Tests.Protocol;

public sealed class PairingBundleCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTrip()
    {
        var bundle = new PairingBundleV1(
            ManagerUrl: "ws://127.0.0.1:27100/ws",
            PairingCode: "ABCD-EFGH",
            Role: "admin",
            ExpiresAtUtc: new DateTimeOffset(2026, 2, 7, 0, 0, 0, TimeSpan.Zero),
            OverlayKind: "tailscale",
            OverlayJoinToken: "tskey-abc",
            OverlayHint: "100.64.0.1");

        var encoded = PairingBundleCodec.Encode(bundle);
        Assert.StartsWith(PairingBundleCodec.Prefix, encoded);

        Assert.True(PairingBundleCodec.TryDecode(encoded, out var decoded, out var error), error);
        Assert.NotNull(decoded);
        Assert.Equal(bundle.ManagerUrl, decoded!.ManagerUrl);
        Assert.Equal(bundle.PairingCode, decoded.PairingCode);
        Assert.Equal(bundle.Role, decoded.Role);
        Assert.Equal(bundle.ExpiresAtUtc, decoded.ExpiresAtUtc);
        Assert.Equal(bundle.OverlayKind, decoded.OverlayKind);
        Assert.Equal(bundle.OverlayJoinToken, decoded.OverlayJoinToken);
        Assert.Equal(bundle.OverlayHint, decoded.OverlayHint);
    }

    [Fact]
    public void TryDecode_AllowsMissingPrefix()
    {
        var bundle = new PairingBundleV1(
            ManagerUrl: "ws://127.0.0.1:27100/ws",
            PairingCode: "ABCD-EFGH",
            Role: "readonly",
            ExpiresAtUtc: new DateTimeOffset(2026, 2, 7, 0, 0, 0, TimeSpan.Zero));

        var encoded = PairingBundleCodec.Encode(bundle);
        var payloadOnly = encoded[PairingBundleCodec.Prefix.Length..];
        Assert.True(PairingBundleCodec.TryDecode(payloadOnly, out var decoded, out var error), error);
        Assert.Equal(bundle.ManagerUrl, decoded!.ManagerUrl);
    }

    [Fact]
    public void TryDecode_RejectsEmpty()
    {
        Assert.False(PairingBundleCodec.TryDecode("   ", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TryDecode_RejectsGarbage()
    {
        Assert.False(PairingBundleCodec.TryDecode("PZMB1:$$$", out _, out _));
    }
}

