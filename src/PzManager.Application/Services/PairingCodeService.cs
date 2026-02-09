using PzManager.Application.Abstractions;
using PzManager.Application.Errors;
using PzManager.Domain.Pairing;
using PzManager.Domain.Security;

namespace PzManager.Application.Services;

public sealed class PairingCodeService
{
    private readonly IClock _clock;
    private readonly IPairingInvitationRepository _pairings;
    private readonly IRandomBytesGenerator _random;

    public PairingCodeService(IClock clock, IPairingInvitationRepository pairings, IRandomBytesGenerator random)
    {
        _clock = clock;
        _pairings = pairings;
        _random = random;
    }

    public async Task<PairingInvitation> CreateAsync(Role role, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new AppException(AppErrorCodes.BadRequest, "ttlSeconds must be > 0");
        }

        var now = _clock.UtcNow;
        var bytes = _random.GetBytes(8);
        var code = new PairingCode(Convert.ToHexString(bytes).ToUpperInvariant());
        var invitation = new PairingInvitation(
            Code: code,
            Role: role,
            CreatedAtUtc: now,
            ExpiresAtUtc: now.Add(ttl),
            ConsumedAtUtc: null);

        await _pairings.UpsertAsync(invitation, cancellationToken);
        return invitation;
    }

    public async Task<PairingInvitation> GetOrCreateBootstrapAdminAsync(TimeSpan ttl, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var invitations = await _pairings.ListAsync(cancellationToken);
        var activeAdmin = invitations
            .Where(i => i.Role == Role.Admin && !i.IsConsumed && !i.IsExpired(now))
            .OrderByDescending(i => i.CreatedAtUtc)
            .FirstOrDefault();

        return activeAdmin ?? await CreateAsync(Role.Admin, ttl, cancellationToken);
    }
}
