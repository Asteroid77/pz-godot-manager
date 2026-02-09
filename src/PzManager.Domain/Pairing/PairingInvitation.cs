using PzManager.Domain.Security;

namespace PzManager.Domain.Pairing;

public sealed record PairingInvitation(
    PairingCode Code,
    Role Role,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc)
{
    public bool IsConsumed => ConsumedAtUtc is not null;

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;
}

