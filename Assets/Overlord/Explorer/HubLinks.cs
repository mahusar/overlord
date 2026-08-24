namespace Overlord.Explorer
{
    public class HubLink
    {
        public string Label;
        public string Url;
        public string Note;

        public HubLink(string label, string url, string note)
        {
            Label = label;
            Url = url;
            Note = note;
        }
    }

    public static class HubLinks
    {
        public static readonly HubLink[] Socials =
        {
            new HubLink("Discord", "https://discord.com/invite/qGwMQQeRPm", "the busiest room"),
            new HubLink("X", "https://twitter.com/stealthsend", ""),
            new HubLink("Telegram", "https://t.me/stealthsend", ""),
            new HubLink("Reddit", "https://www.reddit.com/r/StealthSend/", ""),
            new HubLink("Medium", "https://medium.com/stealthsend", "long form posts"),
            new HubLink("Website", "https://stealth.org/", ""),
            new HubLink("StealthMonitor", "https://www.stealthmonitor.org/", "the network monitor")
        };

        public static readonly HubLink[] Tools =
        {
            new HubLink("stealth-agent-tools",
                "https://github.com/Stealth-R-D-LLC/stealth-agent-tools",
                "Tooling that lets AI agents use Stealth as a payment rail. Ships an MCP server "
                + "with 30 read only tools for querying chain state, stakers and addresses, plus an "
                + "x402 compatible paywall that gates any API behind XST payments."),

            new HubLink("stealth-key-tool",
                "https://github.com/Stealth-R-D-LLC/stealth-key-tool",
                "CLI utility for deriving keys and addresses from a BIP39 mnemonic. Supports BIP44 "
                + "derivation for any coin, WIF ready private keys, and phrase generation and "
                + "validation. Minimal dependencies, meant to be run on an air gapped machine."),

            new HubLink("stealthjs-lib",
                "https://github.com/barrage/stealthjs-lib",
                "JavaScript client for the Stealth RPC daemon, published on npm. Covers the full "
                + "surface, wallet, blockchain queries, address indexing and qPoS staker commands, "
                + "with a generic request() escape hatch for anything not wrapped."),

            new HubLink("xst-dotnet",
                "https://github.com/mahusar/xst-dotnet",
                "NET client for the Stealth RPC daemon, one netstandard2.0 assembly for Unity and "
                + "NET. Covers 103 of 129 methods, wallet, blockchain queries, address indexing, "
                + "extended keys and qPoS staker queries, with a generic InvokeAsync() escape hatch "
                + "for anything not wrapped."),

            new HubLink("stealth-unity-sdk",
                "https://github.com/mahusar/stealth-unity-sdk",
                "Unity toolkit for integrating XST into a game, with a built in wallet UI. Talks to "
                + "a local StealthCoind over JSON-RPC."),

            new HubLink("stealth-daemon-tool",
                "https://github.com/mahusar/stealth-daemon-tool",
                "Shell script for managing common StealthCoind tasks, so routine node operations do "
                + "not have to be typed out by hand.")
        };

        public static readonly HubLink[] Markets =
        {
            new HubLink("NOFINEX", "https://www.nofinex.com/market/pair/XST-USDT.html",
                "XST / USDT, the only market CoinPaprika lists")
        };

        public static readonly HubLink[] Wallets =
        {
            new HubLink("StealthSend desktop",
                "https://github.com/Stealth-R-D-LLC/stealthsend-desktop/releases",
                "Windows, macOS and Linux"),
            new HubLink("iOS", "https://apps.apple.com/app/stealthsend/id1555497657", ""),
            new HubLink("Android",
                "https://play.google.com/store/apps/details?id=com.stealth.wallet", ""),
            new HubLink("Applications page", "https://stealth.org/apps/", "the official list")
        };

        public static readonly HubLink[] Official =
        {
            new HubLink("Stealth on GitHub", "https://github.com/Stealth-R-D-LLC", "the daemon and wallets"),
            new HubLink("Daemon source", "https://github.com/Stealth-R-D-LLC/Stealth", ""),
            new HubLink("qPoS whitepaper",
                "https://github.com/Stealth-R-D-LLC/Stealth/wiki/Stealth-Quantum-PoS-(qPoS)", "")
        };

        public static readonly HubLink[] Mine =
        {
            new HubLink("Overlord", "https://github.com/mahusar/overlord", "this application"),
            new HubLink("xst-dotnet", "https://github.com/mahusar/xst-dotnet",
                "the RPC client underneath"),
            new HubLink("Unity SDK", "https://github.com/mahusar/stealth-unity-sdk", ""),
            new HubLink("StealthDragons", "https://github.com/mahusar/StealthDragons", "the card game"),
            new HubLink("Dragonator add-ons", "https://github.com/mahusar/dragonator-addons", ""),
            new HubLink("Tor transport", "https://github.com/mahusar/mirror-tor-transport", "")
        };
    }
}
