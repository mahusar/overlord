using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overlord.Tor
{
    public static class TorConfig
    {
        public const string OnionAddressKey = "overlord.onion";
        public const string DefaultSocksHost = "127.0.0.1";
        public const int DefaultSocksPort = 9050;
        public const int DefaultHubPort = 7790;

        private static string socksHost = DefaultSocksHost;
        private static int socksPort = DefaultSocksPort;

        public static string SocksHost
        {
            get { return socksHost; }
        }

        public static int SocksPort
        {
            get { return socksPort; }
        }

        public static void SetSocksProxy(string host, int port)
        {
            socksHost = string.IsNullOrEmpty(host) ? DefaultSocksHost : host;
            socksPort = port <= 0 ? DefaultSocksPort : port;
            Debug.Log("[TorConfig] SOCKS proxy is " + socksHost + ":" + socksPort);
        }

        public static string GetSavedOnionAddress()
        {
            return PlayerPrefs.GetString(OnionAddressKey, string.Empty);
        }

        public static void SaveOnionAddress(string address)
        {
            PlayerPrefs.SetString(OnionAddressKey, address ?? string.Empty);
            PlayerPrefs.Save();
        }

        public static List<string> Entries(string text)
        {
            var entries = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return entries;
            }

            string[] parts = text.Split(new char[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    entries.Add(trimmed);
                }
            }

            return entries;
        }

        public static bool TrySplit(string entry, out string onion, out int port)
        {
            onion = null;
            port = DefaultHubPort;

            if (string.IsNullOrEmpty(entry))
            {
                return false;
            }

            string text = entry.Trim();
            int colon = text.LastIndexOf(':');
            if (colon > 0)
            {
                string tail = text.Substring(colon + 1);
                int parsed;
                if (int.TryParse(tail, out parsed) && parsed > 0 && parsed <= 65535)
                {
                    port = parsed;
                    text = text.Substring(0, colon);
                }
            }

            text = text.Trim().ToLowerInvariant();
            if (text.StartsWith("http://"))
            {
                text = text.Substring(7);
            }
            text = text.TrimEnd('/');

            if (!text.EndsWith(".onion") || text.Length != 62)
            {
                return false;
            }

            onion = text;
            return true;
        }
    }
}
