namespace PzManager.Domain.Security;

public static class Capabilities
{
    public const string PairingCreate = "pairing_create";
    public const string DevicesRead = "devices_read";
    public const string DevicesRevoke = "devices_revoke";
    public const string DevicesWrite = "devices_write";

    public const string GameConnect = "game_connect";
    public const string ConfigRead = "config_read";
    public const string ConfigWrite = "config_write";
    public const string LogsRead = "logs_read";
    public const string ServerRestart = "server_restart";
    public const string AdminCmd = "admin_cmd";
}
