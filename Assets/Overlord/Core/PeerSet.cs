using System;
using System.Collections.Generic;

namespace Overlord
{
    public class PeerSet
    {
        public const int DefaultMaxPeers = 512;

        private readonly Dictionary<string, PeerRecord> peers =
            new Dictionary<string, PeerRecord>(StringComparer.Ordinal);

        private readonly int maxPeers;

        public PeerSet() : this(DefaultMaxPeers)
        {
        }

        public PeerSet(int maxPeers)
        {
            this.maxPeers = maxPeers < 1 ? DefaultMaxPeers : maxPeers;
        }

        public int Count
        {
            get { return peers.Count; }
        }

        public bool Merge(PeerRecord incoming)
        {
            if (incoming == null || !incoming.LooksValid())
            {
                return false;
            }

            PeerRecord existing;
            if (peers.TryGetValue(incoming.Key, out existing))
            {
                if (incoming.seen <= existing.seen)
                {
                    return false;
                }
                peers[incoming.Key] = incoming;
                return true;
            }

            if (peers.Count >= maxPeers)
            {
                if (!DropStalest(incoming.seen))
                {
                    return false;
                }
            }

            peers[incoming.Key] = incoming;
            return true;
        }

        public int MergeAll(IEnumerable<PeerRecord> incoming)
        {
            if (incoming == null) return 0;
            int added = 0;
            foreach (PeerRecord p in incoming)
            {
                if (Merge(p)) added++;
            }
            return added;
        }

        public List<PeerRecord> Newest(int count)
        {
            var all = new List<PeerRecord>(peers.Values);
            all.Sort(delegate(PeerRecord a, PeerRecord b) { return b.seen.CompareTo(a.seen); });
            if (count > 0 && all.Count > count)
            {
                all.RemoveRange(count, all.Count - count);
            }
            return all;
        }

        private bool DropStalest(long incomingSeen)
        {
            string stalestKey = null;
            long stalestSeen = long.MaxValue;
            foreach (KeyValuePair<string, PeerRecord> kv in peers)
            {
                if (kv.Value.seen < stalestSeen)
                {
                    stalestSeen = kv.Value.seen;
                    stalestKey = kv.Key;
                }
            }

            if (stalestKey == null || stalestSeen >= incomingSeen)
            {
                return false;
            }

            peers.Remove(stalestKey);
            return true;
        }
    }
}
