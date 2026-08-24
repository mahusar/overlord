using System;

namespace Overlord
{
    [Serializable]
    public class PeerRecord
    {
        public string onion;
        public int port;
        public long height;
        public string version;
        public long seen;

        public PeerRecord()
        {
        }

        public PeerRecord(string onion, int port)
        {
            this.onion = onion;
            this.port = port;
        }

        public bool LooksValid()
        {
            if (string.IsNullOrEmpty(onion)) return false;
            if (!onion.EndsWith(".onion", StringComparison.OrdinalIgnoreCase)) return false;
            if (onion.Length != 62) return false;
            if (port <= 0 || port > 65535) return false;
            return true;
        }

        public string Key
        {
            get { return (onion ?? string.Empty).ToLowerInvariant() + ":" + port; }
        }

        public override string ToString()
        {
            return Key;
        }
    }
}
