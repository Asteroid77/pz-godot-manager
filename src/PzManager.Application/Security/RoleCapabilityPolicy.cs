using PzManager.Domain.Security;

namespace PzManager.Application.Security;

public static class RoleCapabilityPolicy
{
    public static string[] CapabilitiesFor(Role role)
    {
        return role switch
        {
            Role.Player => [Capabilities.GameConnect],
            Role.Readonly => [Capabilities.GameConnect, Capabilities.ConfigRead, Capabilities.LogsRead],
            Role.Gm => [Capabilities.GameConnect, Capabilities.ConfigRead, Capabilities.LogsRead, Capabilities.AdminCmd],
            Role.Ops => [Capabilities.GameConnect, Capabilities.ConfigRead, Capabilities.ConfigWrite, Capabilities.LogsRead, Capabilities.ServerRestart],
            Role.Admin => [
                Capabilities.PairingCreate,
                Capabilities.DevicesRead,
                Capabilities.DevicesRevoke,
                Capabilities.DevicesWrite,
                Capabilities.GameConnect,
                Capabilities.ConfigRead,
                Capabilities.ConfigWrite,
                Capabilities.LogsRead,
                Capabilities.ServerRestart,
                Capabilities.AdminCmd
            ],
            _ => [],
        };
    }
}
