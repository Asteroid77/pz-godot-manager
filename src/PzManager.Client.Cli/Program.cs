using System.Text.Json;
using PzManager.Client;
using PzManager.Client.Persistence;
using PzManager.Transport.Protocol;

static string? Arg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static bool HasArg(string[] args, string name) =>
    args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

static string GetRequired(string? value, string name)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new ArgumentException($"{name} required");
    }

    return value;
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  pzmanager-cli [--url <ws://host:port/ws>] [--pairing-bundle <PZMB1:...>] [--data-dir <dir>] [--device-name <name>] [--pairing-code <code>] <cmd>");
    Console.Error.WriteLine("");
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  auth");
    Console.Error.WriteLine("  whoami");
    Console.Error.WriteLine("  pairing create --role <player|readonly|gm|ops|admin> --ttl <seconds> [--print-bundle] [--bundle-url <ws://host:port/ws>]");
    Console.Error.WriteLine("  devices list");
    Console.Error.WriteLine("  devices find --name <text>");
    Console.Error.WriteLine("  devices update --device-id <id> [--role <player|readonly|gm|ops|admin>] [--note <text>]");
    Console.Error.WriteLine("  devices revoke --device-id <id> [--note <text>]");
    Console.Error.WriteLine("  server status|start|stop|restart [--reason <text>]");
    Console.Error.WriteLine("  logs tail [--max <lines>]");
    Console.Error.WriteLine("  logs follow [--tail <lines>] [--batch <lines>] [--since <ts>] [--offset <n>] [--resume]");
}

static string DefaultUrl() => "ws://127.0.0.1:27100/ws";

static string DefaultDataDir() => Path.Combine(Directory.GetCurrentDirectory(), "data-client");

static string KeyFile(string dataDir) => Path.Combine(dataDir, "device-key.json");

static string CursorFile(string dataDir) => Path.Combine(dataDir, "logs.cursor.json");

static LogsCursor? TryLoadCursor(string path)
{
    if (!File.Exists(path))
    {
        return null;
    }

    try
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LogsCursor>(json);
    }
    catch
    {
        return null;
    }
}

static void SaveCursor(string path, LogsCursor cursor)
{
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(dir))
    {
        Directory.CreateDirectory(dir);
    }

    var json = JsonSerializer.Serialize(cursor, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json + "\n");
}

try
{
    var url = Arg(args, "--url") ?? DefaultUrl();
    var dataDir = Arg(args, "--data-dir") ?? DefaultDataDir();
    var deviceName = Arg(args, "--device-name");
    var pairingCode = Arg(args, "--pairing-code");
    var pairingBundle = Arg(args, "--pairing-bundle");
    if (!string.IsNullOrWhiteSpace(pairingBundle))
    {
        if (!PairingBundleCodec.TryDecode(pairingBundle, out var decoded, out var decodeError))
        {
            throw new ArgumentException($"invalid --pairing-bundle: {decodeError}");
        }

        url = decoded!.ManagerUrl;
        pairingCode = decoded.PairingCode;
    }

    var remaining = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (a.StartsWith("--", StringComparison.Ordinal))
        {
            if (string.Equals(a, "--resume", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(a, "--print-bundle", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            i++;
            continue;
        }

        remaining.Add(a);
    }

    if (remaining.Count == 0)
    {
        PrintUsage();
        return 2;
    }

    var cmd0 = remaining[0].ToLowerInvariant();
    var cmd1 = remaining.Count > 1 ? remaining[1].ToLowerInvariant() : null;

    var key = DeviceKeyFileStore.LoadOrCreate(KeyFile(dataDir));
    var options = new ManagerWsClientOptions(new Uri(url), key, deviceName, pairingCode);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await using var client = await ManagerWsClient.ConnectAsync(options, cts.Token);

    if (cmd0 == "auth")
    {
        Console.WriteLine(JsonSerializer.Serialize(client.Auth, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    if (cmd0 == "whoami")
    {
        var who = await client.WhoAmIAsync(cts.Token);
        Console.WriteLine(JsonSerializer.Serialize(who, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    if (cmd0 == "pairing" && cmd1 == "create")
    {
        var role = GetRequired(Arg(args, "--role"), "--role");
        var ttlRaw = GetRequired(Arg(args, "--ttl"), "--ttl");
        if (!int.TryParse(ttlRaw, out var ttlSeconds))
        {
            throw new ArgumentException("invalid --ttl");
        }

        var ok = await client.PairingCreateAsync(role, ttlSeconds, cts.Token);

        if (HasArg(args, "--print-bundle"))
        {
            var bundleUrl = Arg(args, "--bundle-url") ?? url;
            var bundle = new PairingBundleV1(bundleUrl, ok.PairingCode, role, ok.ExpiresAtUtc);
            Console.WriteLine(PairingBundleCodec.Encode(bundle));
            return 0;
        }

        Console.WriteLine(JsonSerializer.Serialize(ok, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    if (cmd0 == "devices" && cmd1 == "list")
    {
        var ok = await client.DevicesListAsync(cts.Token);
        Console.WriteLine(JsonSerializer.Serialize(ok, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    if (cmd0 == "devices" && cmd1 == "find")
    {
        var name = GetRequired(Arg(args, "--name"), "--name");
        var ok = await client.DevicesListAsync(cts.Token);
        var device = ok.Devices.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.Name) && string.Equals(d.Name, name, StringComparison.Ordinal));

        if (device is null)
        {
            Console.Error.WriteLine("not found");
            return 3;
        }

        Console.WriteLine(device.DeviceId);
        return 0;
    }

    if (cmd0 == "devices" && cmd1 == "update")
    {
        var deviceId = GetRequired(Arg(args, "--device-id"), "--device-id");
        var role = Arg(args, "--role");
        var note = Arg(args, "--note");
        if (role is null && note is null)
        {
            throw new ArgumentException("--role or --note required");
        }

        var ok = await client.DevicesUpdateAsync(deviceId, role, note, cts.Token);
        Console.WriteLine(JsonSerializer.Serialize(ok, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    if (cmd0 == "devices" && cmd1 == "revoke")
    {
        var deviceId = GetRequired(Arg(args, "--device-id"), "--device-id");
        var note = Arg(args, "--note");
        var ok = await client.DevicesRevokeAsync(deviceId, note, cts.Token);
        Console.WriteLine(JsonSerializer.Serialize(ok, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    if (cmd0 == "server" && cmd1 == "status")
    {
        var ok = await client.ServerStatusAsync(cts.Token);
        Console.WriteLine(JsonSerializer.Serialize(ok, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    if (cmd0 == "server" && (cmd1 == "start" || cmd1 == "stop" || cmd1 == "restart"))
    {
        var reason = Arg(args, "--reason");
        ServerControlOkV1 ok = cmd1 switch
        {
            "start" => await client.ServerStartAsync(reason, cts.Token),
            "stop" => await client.ServerStopAsync(reason, cts.Token),
            _ => await client.ServerRestartAsync(reason, cts.Token),
        };

        Console.WriteLine(JsonSerializer.Serialize(ok, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    if (cmd0 == "logs" && cmd1 == "tail")
    {
        var maxRaw = Arg(args, "--max") ?? "200";
        if (!int.TryParse(maxRaw, out var maxLines))
        {
            throw new ArgumentException("invalid --max");
        }

        var ok = await client.LogsTailAsync(maxLines, cts.Token);
        foreach (var line in ok.Lines)
        {
            Console.WriteLine(line);
        }

        return 0;
    }

    if (cmd0 == "logs" && cmd1 == "follow")
    {
        var tailRaw = Arg(args, "--tail") ?? "200";
        var batchRaw = Arg(args, "--batch") ?? "50";
        if (!int.TryParse(tailRaw, out var tailLines))
        {
            throw new ArgumentException("invalid --tail");
        }

        if (!int.TryParse(batchRaw, out var batchLines))
        {
            throw new ArgumentException("invalid --batch");
        }

        var resume = HasArg(args, "--resume");
        var cursorPath = CursorFile(dataDir);
        var cursor = resume ? TryLoadCursor(cursorPath) : null;

        var offsetRaw = Arg(args, "--offset");
        long? offset = null;
        if (offsetRaw is not null)
        {
            if (!long.TryParse(offsetRaw, out var parsed))
            {
                throw new ArgumentException("invalid --offset");
            }

            offset = parsed;
        }

        var since = Arg(args, "--since");
        var req = new LogsFollowRequestV1(
            TailLines: tailLines,
            BatchLines: batchLines,
            Since: since ?? cursor?.Since,
            Offset: offset ?? cursor?.Offset);

        var ack = await client.LogsFollowStartAsync(req, cts.Token);
        var followId = ack.FollowId;
        Console.Error.WriteLine($"[follow] started: followId={followId}");

        var nextOffset = ack.NextOffset;
        var nextSince = ack.NextSince;
        if (nextOffset is not null || nextSince is not null)
        {
            SaveCursor(cursorPath, new LogsCursor(nextOffset, nextSince));
        }

        try
        {
            await foreach (var msg in client.LogsFollowMessages(followId).WithCancellation(cts.Token))
            {
                foreach (var line in msg.Lines)
                {
                    Console.WriteLine(line);
                }

                nextOffset = msg.NextOffset ?? nextOffset;
                nextSince = msg.NextSince ?? nextSince;
                if (nextOffset is not null || nextSince is not null)
                {
                    SaveCursor(cursorPath, new LogsCursor(nextOffset, nextSince));
                }

                if (msg.IsEnd)
                {
                    Console.Error.WriteLine($"[follow] end: reason={msg.Reason ?? ""}");
                    break;
                }
            }
        }
        finally
        {
            try
            {
                await client.LogsFollowStopAsync(followId, CancellationToken.None);
            }
            catch
            {
                // ignore
            }
        }

        return 0;
    }

    PrintUsage();
    return 2;
}
catch (ManagerWsException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Code}: {ex.Message}");
    if (!string.IsNullOrWhiteSpace(ex.Details))
    {
        Console.Error.WriteLine(ex.Details);
    }

    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

internal sealed record LogsCursor(long? Offset, string? Since);
