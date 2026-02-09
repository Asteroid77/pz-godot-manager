namespace PzManager.Transport.Protocol;

public static class WsMessageTypes
{
    public const string Hello = "hello";
    public const string Auth = "auth";
    public const string AuthOk = "auth.ok";
    public const string Error = "error";

    public const string Ping = "ping";
    public const string Pong = "pong";

    public const string PairingCreate = "pairing.create";
    public const string PairingCreateOk = "pairing.create.ok";

    public const string DevicesList = "devices.list";
    public const string DevicesListOk = "devices.list.ok";
    public const string DevicesRevoke = "devices.revoke";
    public const string DevicesRevokeOk = "devices.revoke.ok";
    public const string DevicesUpdate = "devices.update";
    public const string DevicesUpdateOk = "devices.update.ok";

    public const string WhoAmI = "whoami";
    public const string WhoAmIOk = "whoami.ok";

    public const string ServerStatus = "server.status";
    public const string ServerStatusOk = "server.status.ok";
    public const string ServerStart = "server.start";
    public const string ServerStartOk = "server.start.ok";
    public const string ServerStop = "server.stop";
    public const string ServerStopOk = "server.stop.ok";
    public const string ServerRestart = "server.restart";
    public const string ServerRestartOk = "server.restart.ok";

    public const string LogsTail = "logs.tail";
    public const string LogsTailOk = "logs.tail.ok";

    public const string LogsFollow = "logs.follow";
    public const string LogsFollowOk = "logs.follow.ok";
    public const string LogsFollowStop = "logs.follow.stop";
    public const string LogsFollowStopOk = "logs.follow.stop.ok";
}
