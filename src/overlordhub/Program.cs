using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Overlord;
using Overlord.Registry;
using Xst.Rpc;
using Xst.Rpc.Models;

internal static class Program
{
    private const int DefaultHubPort = 7790;
    private const string DefaultBind = "127.0.0.1";
    private const decimal DefaultAmount = 0.01m;

    private static async Task<int> Main(string[] args)
    {
        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        Dictionary<string, string> options = Parse(args);

        try
        {
            switch (command)
            {
                case "serve":
                    return await Serve(options);
                case "check":
                    return await Check(options);
                case "publish":
                    return await Publish(options);
                case "help":
                case "--help":
                case "-h":
                    Usage();
                    return 0;
                default:
                    Console.Error.WriteLine("unknown command: " + command);
                    Usage();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static void Usage()
    {
        Console.WriteLine("overlordhub - a read only Stealth chain hub, served over Tor");
        Console.WriteLine();
        Console.WriteLine("  overlordhub serve     answer hub queries on a local port");
        Console.WriteLine("  overlordhub check     ask the daemon whether it is fit to serve");
        Console.WriteLine("  overlordhub publish   announce this hub on the chain");
        Console.WriteLine();
        Console.WriteLine("daemon, from the environment or the command line:");
        Console.WriteLine("  XST_RPC_HOST      --host        default " + DefaultBind);
        Console.WriteLine("  XST_RPC_PORT      --rpc-port    default 46502");
        Console.WriteLine("  XST_RPC_USER      --user");
        Console.WriteLine("  XST_RPC_PASSWORD  --password");
        Console.WriteLine();
        Console.WriteLine("serve:");
        Console.WriteLine("  OVERLORD_HUB_PORT --port        default " + DefaultHubPort);
        Console.WriteLine("  OVERLORD_BIND     --bind        default " + DefaultBind + ", keep it on loopback");
        Console.WriteLine();
        Console.WriteLine("publish:");
        Console.WriteLine("  OVERLORD_ONION    --onion       your v3 address");
        Console.WriteLine("  OVERLORD_HS_DIR   --hs-dir      read the onion from a hostname file instead");
        Console.WriteLine("  OVERLORD_PASSPHRASE --passphrase   needed if the wallet is encrypted");
        Console.WriteLine("                    --port        the port to advertise, default " + DefaultHubPort);
        Console.WriteLine("                    --flags       2 hub, 1 dragonator, 3 both. default 2");
        Console.WriteLine("                    --amount      XST to send to yourself, default " + DefaultAmount);
        Console.WriteLine("                    --address     send to this address instead of a fresh one");
        Console.WriteLine("                    --feeless     pay with feework instead of a money fee");
        Console.WriteLine("                    --yes         actually publish; without it this is a dry run");
    }

    // ------------------------------------------------------------------ serve

    private static async Task<int> Serve(Dictionary<string, string> options)
    {
        int port = Number(options, "port", "OVERLORD_HUB_PORT", DefaultHubPort);
        string bind = Text(options, "bind", "OVERLORD_BIND", DefaultBind);

        IPAddress address;
        if (!IPAddress.TryParse(bind, out address))
        {
            Console.Error.WriteLine("could not read --bind " + bind);
            return 2;
        }

        using (XstClient client = Connect(options))
        {
            var dispatcher = new HubDispatcher();
            var handlers = new HubHandlers(client, new PeerSet());
            handlers.RegisterAll(dispatcher);

            HubDispatcher.OnInternalError = delegate(string query, Exception problem)
            {
                Console.Error.WriteLine(Stamp() + "  " + query + " threw " +
                    problem.GetType().Name + ": " + problem.Message);
            };

            using (var server = new HubServer(dispatcher, port, address))
            {
                string trouble = await server.StartAsync(handlers.SelfCheckAsync);
                if (trouble != null)
                {
                    Console.Error.WriteLine("refusing to serve: " + trouble);
                    return 1;
                }

                Console.WriteLine("overlordhub is listening on " + bind + ":" +
                    port.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("answers  " + string.Join(" ", new List<string>(HubQueries.All).ToArray()));

                if (server.Notice != null)
                {
                    Console.WriteLine("notice   " + server.Notice);
                }

                if (!IPAddress.IsLoopback(address))
                {
                    Console.WriteLine("WARNING  " + bind + " is not loopback. Anyone who can reach this " +
                        "port can query the hub directly, without Tor.");
                }

                Console.WriteLine();
                Console.WriteLine("to reach it over Tor, put this in your torrc and reload tor:");
                Console.WriteLine("  HiddenServiceDir /var/lib/tor/overlord/");
                Console.WriteLine("  HiddenServicePort " + port + " 127.0.0.1:" + port);
                Console.WriteLine("then the address is in /var/lib/tor/overlord/hostname");
                Console.WriteLine();

                var stopping = new ManualResetEventSlim(false);
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
                {
                    e.Cancel = true;
                    stopping.Set();
                };

                while (!stopping.Wait(30000))
                {
                    Console.WriteLine(Stamp() +
                        "  connections " + server.Connections +
                        "  served " + server.Served +
                        "  limited " + server.Limited +
                        "  refused " + server.Refused);
                }

                Console.WriteLine("stopping");
            }
        }

        return 0;
    }

    // ------------------------------------------------------------------ check

    private static async Task<int> Check(Dictionary<string, string> options)
    {
        using (XstClient client = Connect(options))
        {
            var dispatcher = new HubDispatcher();
            var handlers = new HubHandlers(client, new PeerSet());
            handlers.RegisterAll(dispatcher);

            string trouble = await handlers.SelfCheckAsync();
            if (trouble != null)
            {
                Console.Error.WriteLine("not fit to serve: " + trouble);
                return 1;
            }

            XstInfo info = await client.GetInfoAsync();
            Console.WriteLine("daemon      " + info.Version + "  height " +
                info.Blocks.ToString("N0", CultureInfo.InvariantCulture));
            Console.WriteLine("allowlist   " + (HubQueries.AuditAllowlist() ?? "passes"));
            Console.WriteLine("unhandled   " + (dispatcher.Unregistered() ?? "none"));
            Console.WriteLine("fit to serve");
        }

        return 0;
    }

    // ---------------------------------------------------------------- publish

    private static async Task<int> Publish(Dictionary<string, string> options)
    {
        string onion = Text(options, "onion", "OVERLORD_ONION", null);

        if (string.IsNullOrEmpty(onion))
        {
            string directory = Text(options, "hs-dir", "OVERLORD_HS_DIR", null);
            if (!string.IsNullOrEmpty(directory))
            {
                string file = Path.Combine(directory, "hostname");
                if (!File.Exists(file))
                {
                    Console.Error.WriteLine("no hostname file at " + file);
                    return 2;
                }
                onion = File.ReadAllText(file).Trim();
                Console.WriteLine("read " + onion + " from " + file);
            }
        }

        if (string.IsNullOrEmpty(onion))
        {
            Console.Error.WriteLine("give --onion or --hs-dir");
            return 2;
        }

        int port = Number(options, "port", "OVERLORD_HUB_PORT", DefaultHubPort);
        int flags = Number(options, "flags", null, OnionListing.FlagHub);

        decimal amount = DefaultAmount;
        string wanted = Text(options, "amount", null, null);
        if (!string.IsNullOrEmpty(wanted) &&
            !decimal.TryParse(wanted, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        {
            Console.Error.WriteLine("could not read --amount " + wanted);
            return 2;
        }

        string payload = OnionListing.Encode(onion, port, flags);
        if (payload == null)
        {
            Console.Error.WriteLine("that is not a v3 onion address, or the port is out of range");
            return 2;
        }

        OnionListing readBack = OnionListing.Decode(payload);
        if (readBack == null || readBack.Onion != onion || readBack.Port != port)
        {
            Console.Error.WriteLine("the record did not decode back to the same address, refusing to publish");
            return 1;
        }

        Console.WriteLine("record    " + payload);
        Console.WriteLine("bytes     " + (payload.Length / 2));
        Console.WriteLine("decodes   " + readBack.Entry + "  flags " + readBack.Flags);

        if (!options.ContainsKey("yes"))
        {
            Console.WriteLine();
            Console.WriteLine("This is a dry run. Publishing spends " +
                amount.ToString(CultureInfo.InvariantCulture) +
                " XST and writes your onion address into the chain permanently, where it");
            Console.WriteLine("cannot be removed and can be linked to the address that paid for it.");
            Console.WriteLine("Re-run with --yes when you mean it.");
            return 0;
        }

        using (XstClient client = Connect(options))
        {
            string passphrase = Text(options, "passphrase", "OVERLORD_PASSPHRASE", null);
            if (!string.IsNullOrEmpty(passphrase))
            {
                await client.WalletPassphraseAsync(passphrase, TimeSpan.FromSeconds(120));
                Console.WriteLine("wallet    unlocked for two minutes");
            }

            string target = Text(options, "address", null, null);
            if (string.IsNullOrEmpty(target))
            {
                target = await client.GetNewAddressAsync("overlord registry");
                Console.WriteLine("paying    a fresh address of your own, " + target);
            }
            else
            {
                Console.WriteLine("paying    " + target);
            }

            bool feeless = options.ContainsKey("feeless");
            string txid = await client.SendToAddressAsync(target, amount,
                "overlord registry", string.Empty, feeless, new string[] { payload });

            Console.WriteLine("published " + txid);
            Console.WriteLine();
            Console.WriteLine("check it with:  getrawtransaction " + txid + " 1");
            Console.WriteLine("other hubs and clients will see it once it is in a block.");
        }

        return 0;
    }

    // ----------------------------------------------------------------- pieces

    private static XstClient Connect(Dictionary<string, string> options)
    {
        var settings = new XstClientOptions
        {
            Host = Text(options, "host", "XST_RPC_HOST", DefaultBind),
            Port = Number(options, "rpc-port", "XST_RPC_PORT", 46502),
            Username = Text(options, "user", "XST_RPC_USER", null),
            Password = Text(options, "password", "XST_RPC_PASSWORD", null)
        };

        return new XstClient(settings);
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < args.Length; i++)
        {
            string argument = args[i];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string key = argument.Substring(2);
            int equals = key.IndexOf('=');

            if (equals > 0)
            {
                options[key.Substring(0, equals)] = key.Substring(equals + 1);
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = args[i + 1];
                i++;
            }
            else
            {
                options[key] = string.Empty;
            }
        }

        return options;
    }

    private static string Text(Dictionary<string, string> options, string key, string variable, string fallback)
    {
        string value;
        if (options.TryGetValue(key, out value) && value.Length > 0)
        {
            return value;
        }

        if (!string.IsNullOrEmpty(variable))
        {
            string environment = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(environment))
            {
                return environment;
            }
        }

        return fallback;
    }

    private static int Number(Dictionary<string, string> options, string key, string variable, int fallback)
    {
        string text = Text(options, key, variable, null);
        int parsed;
        if (!string.IsNullOrEmpty(text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static string Stamp()
    {
        return DateTime.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
