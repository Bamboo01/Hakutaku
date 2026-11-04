using simulator;

// Mock players for Hakutaku. Everything goes through the SDK in ../sdk, the same way the
// Unity plugin will use it. See docs/components/simulator.md.

const string Usage = """
    Usage:
      Hakutaku.Simulator probe [--players N] [--url URL] [--admin-password PW]
                               [--server-key KEY] [--revoked-key KEY] [--expired-key KEY]
      Hakutaku.Simulator shell [--url URL]

      (From simulator/ with the .NET SDK: dotnet run -- probe, dotnet run -- shell.)

      probe     Registers N mock players (default 3), takes the first through the account
                flows, then has each one try every route beyond player auth. Prints what
                each step got against what it should get, and exits 1 if anything differed.
      shell     Interactive: make mock players and call any SDK method by hand. Use it for
                the steps that need an emailed code (with no SMTP, it is in the server log).

      --url     The server. Default http://localhost:5008, or $HAKUTAKU_URL if set.
      --admin-password
                The owner's password (the username is admin), or $HAKUTAKU_ADMIN_PASSWORD.
                With it, probe also makes server keys and checks they open /api/server only.
                It revokes the keys again. Without it that part is skipped.
      --server-key
                A live server key from the keys page, or $HAKUTAKU_SERVER_KEY (the variable
                keeps it out of your shell history). For production, where the probe can't
                make keys: it checks the key opens /api/server and not /api/admin, then
                validates a ticket and writes and reads Xp through the server SDK.
      --revoked-key, --expired-key
                A key you revoked, and one past its expiry. Each is checked to be refused.
    """;

var url = Environment.GetEnvironmentVariable("HAKUTAKU_URL") ?? "http://localhost:5008";
var playerCount = 3;
var adminPassword = Environment.GetEnvironmentVariable("HAKUTAKU_ADMIN_PASSWORD");
var serverKey = Environment.GetEnvironmentVariable("HAKUTAKU_SERVER_KEY");
string? revokedKey = null;
string? expiredKey = null;
string? command = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "probe" or "shell" when command is null:
            command = args[i];
            break;
        case "--url" when i + 1 < args.Length:
            url = args[++i];
            break;
        case "--admin-password" when i + 1 < args.Length:
            adminPassword = args[++i];
            break;
        case "--server-key" when i + 1 < args.Length:
            serverKey = args[++i];
            break;
        case "--revoked-key" when i + 1 < args.Length:
            revokedKey = args[++i];
            break;
        case "--expired-key" when i + 1 < args.Length:
            expiredKey = args[++i];
            break;
        case "--players" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0:
            playerCount = n;
            i++;
            break;
        case "-h" or "--help" or "help":
            Console.WriteLine(Usage);
            return 0;
        default:
            Console.Error.WriteLine($"Unexpected argument '{args[i]}'.");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

if (command is null)
{
    Console.Error.WriteLine("Missing a command: give 'probe' or 'shell' (the --url alone isn't enough).");
    Console.Error.WriteLine();
    Console.Error.WriteLine(Usage);
    return 2;
}

if (!Uri.TryCreate(url, UriKind.Absolute, out var server) || (server.Scheme != "http" && server.Scheme != "https"))
{
    Console.Error.WriteLine($"'{url}' isn't an http(s) URL.");
    return 2;
}

// No route deletes a player, so whatever this makes stays in that database for good.
if (!server.IsLoopback)
    Report.Line(ConsoleColor.Yellow, $"Note: {server.Host} isn't this machine. Every mock player made here is a permanent row in its database.");

if (command == "shell")
{
    await new Shell(url).RunAsync();
    return 0;
}

Console.WriteLine($"Probing {url}");
// unset and blank both mean "not given". passed through as typed: a password may start or end with a space
static string? Given(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
return await Probe.RunAsync(url, playerCount, Given(adminPassword), new GivenKeys(Given(serverKey), Given(revokedKey), Given(expiredKey)));
