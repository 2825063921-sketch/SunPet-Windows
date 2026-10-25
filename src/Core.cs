using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace MemePetsWindows
{
    public sealed class ShuffleBag
    {
        readonly int[] values;
        readonly Random random;
        int position;
        int previous = -1;
        public ShuffleBag(int count, Random random)
        {
            if (count < 1) throw new ArgumentOutOfRangeException("count");
            if (random == null) throw new ArgumentNullException("random");
            this.random = random; values = new int[count]; position = count;
        }
        public int Next()
        {
            if (position == values.Length)
            {
                for (int i = 0; i < values.Length; i++) values[i] = i;
                for (int i = values.Length - 1; i > 0; i--)
                { int other = random.Next(i + 1); int swap = values[i]; values[i] = values[other]; values[other] = swap; }
                if (values.Length > 1 && values[0] == previous)
                { int swap = values[0]; values[0] = values[1]; values[1] = swap; }
                position = 0;
            }
            previous = values[position++]; return previous;
        }
    }
    public sealed class PlaybackGate
    {
        public bool IsBusy { get; private set; }
        public bool TryStart() { if (IsBusy) return false; IsBusy = true; return true; }
        public void Finish() { IsBusy = false; }
    }
    public sealed class LinkMessage
    {
        public string from { get; set; }
        public string root { get; set; }
        public int hop { get; set; }
        public string chainId { get; set; }
        public string utc { get; set; }
    }
    public static class LinkRules
    {
        public static bool IsRole(string value) { return value == "sun" || value == "shaolan" || value == "fengge"; }
        public static LinkMessage Manual(string role)
        { return new LinkMessage { from = role, root = role, hop = 0, chainId = Guid.NewGuid().ToString("D"), utc = DateTime.UtcNow.ToString("o") }; }
        public static LinkMessage Advance(string role, LinkMessage message)
        { return new LinkMessage { from = role, root = message.root, hop = message.hop + 1, chainId = message.chainId, utc = message.utc }; }
        public static string[] Targets(LinkMessage message)
        {
            if (message == null || message.hop >= 3) return new string[0];
            if (message.root == "shaolan")
            {
                if (message.from == "shaolan" && message.hop == 0) return new[] { "sun" };
                if (message.from == "sun" && message.hop == 1) return new[] { "fengge" };
                if (message.from == "fengge" && message.hop == 2) return new[] { "sun" };
            }
            if (message.root == "fengge" && message.from == "fengge" && message.hop == 0) return new[] { "sun" };
            return new string[0];
        }
        public static bool Validate(LinkMessage message, DateTime now, out string reason)
        {
            Guid id; DateTime timestamp;
            if (message == null || !IsRole(message.from) || !IsRole(message.root) || message.hop < 0 || message.hop > 3 ||
                !Guid.TryParseExact(message.chainId, "D", out id) ||
                !DateTime.TryParseExact(message.utc, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp) || timestamp.Kind != DateTimeKind.Utc)
            { reason = "invalid"; return false; }
            double age = (now.ToUniversalTime() - timestamp).TotalSeconds;
            if (age < -2 || age > 30) { reason = "expired"; return false; }
            reason = null; return true;
        }
    }
    public sealed class LinkReceiver
    {
        readonly string role;
        readonly Dictionary<string, DateTime> accepted = new Dictionary<string, DateTime>();
        public LinkReceiver(string role) { if (!LinkRules.IsRole(role)) throw new ArgumentException("role"); this.role = role; }
        public bool TryAccept(LinkMessage message, DateTime now, bool busy, bool enabled, out string reason)
        {
            if (!LinkRules.Validate(message, now, out reason)) return false;
            if (message.from == role) { reason = "self"; return false; }
            if (!LinkRules.Targets(message).Contains(role)) { reason = "route"; return false; }
            foreach (string expired in accepted.Where(pair => (now - pair.Value).TotalSeconds > 31).Select(pair => pair.Key).ToArray()) accepted.Remove(expired);
            string key = message.chainId + ":" + message.hop;
            if (accepted.ContainsKey(key)) { reason = "duplicate"; return false; }
            // Consume even dropped notifications so toggling/busy state never queues a replay.
            accepted[key] = now;
            if (!enabled) { reason = "disabled"; return false; }
            if (busy) { reason = "busy"; return false; }
            reason = null; return true;
        }
    }
    public sealed class PhrasePool
    {
        readonly int[] ids;
        readonly ShuffleBag bag;
        public PhrasePool(int[] ids, bool response, Random random)
        {
            if (ids == null || ids.Length == 0) throw new ArgumentException("At least one phrase is required.");
            int[] enabled = ids.Distinct().OrderBy(value => value).ToArray();
            int[] preferred = new[] { 1, 2, 4, 11, 13, 14 };
            int[] selected = response ? enabled.Where(value => preferred.Contains(value)).ToArray() : enabled;
            this.ids = selected.Length > 0 ? selected : enabled;
            bag = new ShuffleBag(this.ids.Length, random);
        }
        public int Next() { return ids[bag.Next()]; }
    }
}
