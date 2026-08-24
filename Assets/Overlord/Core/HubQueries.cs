using System;
using System.Collections.Generic;

namespace Overlord
{
    public static class HubQueries
    {
        public const string Ping = "ping";
        public const string Peers = "peers";
        public const string GetInfo = "getinfo";
        public const string GetBlock = "getblock";
        public const string GetTransaction = "gettransaction";
        public const string GetAddress = "getaddress";
        public const string GetRichList = "getrichlist";
        public const string ChainStats = "chainstats";
        public const string Mempool = "mempool";
        public const string Series = "series";
        public const string Registry = "registry";
        public const string Volume = "volume";
        public const string Holders = "holders";

        private static readonly HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            Ping,
            Peers,
            GetInfo,
            GetBlock,
            GetTransaction,
            GetAddress,
            GetRichList,
            ChainStats,
            Mempool,
            Series,
            Registry,
            Volume,
            Holders
        };

        private static readonly string[] forbiddenFragments =
        {
            "wallet", "priv", "key", "sign", "send", "move", "sethd", "import",
            "dump", "backup", "encrypt", "passphrase", "account", "stop",
            "sendalert", "addnode", "setgenerate", "purchase", "claim", "staker",
            "reservebalance", "settxfee", "listunspent", "createraw", "signraw",
            "sendraw", "repair", "checkwallet"
        };

        public static bool IsAllowed(string query)
        {
            return !string.IsNullOrEmpty(query) && allowed.Contains(query);
        }

        public static IEnumerable<string> All
        {
            get { return allowed; }
        }

        public static string AuditAllowlist()
        {
            foreach (string q in allowed)
            {
                string lower = q.ToLowerInvariant();
                foreach (string bad in forbiddenFragments)
                {
                    if (lower.Contains(bad))
                    {
                        return "allowlist contains a forbidden query: " + q + " matches '" + bad + "'";
                    }
                }
            }
            return null;
        }
    }
}
