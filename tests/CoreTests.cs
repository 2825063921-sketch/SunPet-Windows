using System;
using System.Collections.Generic;
using MemePetsWindows;

static class CoreTests
{
    static int checks;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static LinkMessage Message(string from, string root, int hop, DateTime now)
    { return new LinkMessage { from = from, root = root, hop = hop, chainId = Guid.NewGuid().ToString("D"), utc = now.ToString("o") }; }
    public static int Main()
    {
        try
        {
            foreach (int count in new[] { 28, 37, 11, 8, 1 })
            {
                var bag = new ShuffleBag(count, new Random(42));
                int previous = -1;
                for (int round = 0; round < 100; round++)
                {
                    var seen = new HashSet<int>();
                    for (int i = 0; i < count; i++)
                    {
                        int next = bag.Next();
                        Check(next >= 0 && next < count, "shuffle bounds");
                        Check(seen.Add(next), "no repeat inside a round");
                        Check(count == 1 || next != previous, "no adjacent repeat across rounds");
                        previous = next;
                    }
                }
            }
            var gate = new PlaybackGate();
            Check(gate.TryStart(), "first playback accepted");
            Check(!gate.TryStart() && gate.IsBusy, "busy playback is rejected without queueing");
            gate.Finish(); Check(gate.TryStart(), "finish releases playback"); gate.Finish();
            DateTime now = DateTime.UtcNow;
            string reason;
            var message = Message("shaolan", "shaolan", 0, now);
            var receiver = new LinkReceiver("sun");
            Check(receiver.TryAccept(message, now, false, true, out reason), "shaolan to sun");
            Check(!receiver.TryAccept(message, now, false, true, out reason) && reason == "duplicate", "duplicate rejected");
            var sun = LinkRules.Advance("sun", message);
            Check(sun.root == "shaolan" && sun.hop == 1 && sun.chainId == message.chainId, "chain preserved");
            Check(new LinkReceiver("fengge").TryAccept(sun, now, false, true, out reason), "sun to fengge for shaolan root");
            var fengge = LinkRules.Advance("fengge", sun);
            Check(new LinkReceiver("sun").TryAccept(fengge, now, false, true, out reason), "fengge to sun final response");
            var final = LinkRules.Advance("sun", fengge);
            Check(final.hop == 3 && LinkRules.Targets(final).Length == 0, "hop 3 stops chain");
            Check(LinkRules.Targets(Message("sun", "sun", 0, now)).Length == 0, "manual sun does not trigger others");
            var manualFengge = Message("fengge", "fengge", 0, now);
            Check(new LinkReceiver("sun").TryAccept(manualFengge, now, false, true, out reason), "manual fengge to sun");
            Check(LinkRules.Targets(LinkRules.Advance("sun", manualFengge)).Length == 0, "manual fengge chain stops at sun");
            Check(!new LinkReceiver("shaolan").TryAccept(message, now, false, true, out reason), "shaolan never responds");
            Check(!new LinkReceiver("fengge").TryAccept(Message("sun", "sun", 0, now), now, false, true, out reason), "fengge only responds to shaolan-root sun");
            Check(!new LinkReceiver("sun").TryAccept(Message("fengge", "sun", 2, now), now, false, true, out reason), "illegal root rejected");
            Check(!new LinkReceiver("sun").TryAccept(message, now, true, true, out reason) && reason == "busy", "busy receiver drops");
            Check(!new LinkReceiver("sun").TryAccept(message, now, false, false, out reason) && reason == "disabled", "disabled receiver drops");
            var busyReceiver = new LinkReceiver("sun");
            var busyMessage = Message("shaolan", "shaolan", 0, now);
            Check(!busyReceiver.TryAccept(busyMessage, now, true, true, out reason) && reason == "busy", "busy notification discarded immediately");
            Check(!busyReceiver.TryAccept(busyMessage, now.AddSeconds(1), false, true, out reason) && reason == "duplicate", "discarded busy notification cannot replay later");
            var disabledReceiver = new LinkReceiver("sun");
            var disabledMessage = Message("shaolan", "shaolan", 0, now);
            Check(!disabledReceiver.TryAccept(disabledMessage, now, false, false, out reason), "disabled notification discarded");
            Check(!disabledReceiver.TryAccept(disabledMessage, now.AddSeconds(1), false, true, out reason) && reason == "duplicate", "enabling links does not replay discarded notification");
            Check(!new LinkReceiver("sun").TryAccept(Message("shaolan", "shaolan", 0, now.AddSeconds(-31)), now, false, true, out reason), "expired rejected");
            Check(!new LinkReceiver("sun").TryAccept(Message("shaolan", "shaolan", 0, now.AddSeconds(4)), now, false, true, out reason), "future rejected");
            foreach (int hop in new[] { -1, 4, 999 }) Check(!new LinkReceiver("sun").TryAccept(Message("shaolan", "shaolan", hop, now), now, false, true, out reason), "illegal hop rejected");
            var invalid = Message("shaolan", "shaolan", 0, now); invalid.chainId = "x";
            Check(!new LinkReceiver("sun").TryAccept(invalid, now, false, true, out reason), "invalid chain id rejected");
            invalid = Message("shaolan", "shaolan", 0, now); invalid.utc = "bad";
            Check(!new LinkReceiver("sun").TryAccept(invalid, now, false, true, out reason), "invalid date rejected");
            Check(!new LinkReceiver("sun").TryAccept(null, now, false, true, out reason), "null rejected");
            var bothPreferred = new PhrasePool(new[] { 4, 11 }, true, new Random(3));
            var bothSeen = new HashSet<int>(); bothSeen.Add(bothPreferred.Next()); bothSeen.Add(bothPreferred.Next());
            Check(bothSeen.SetEquals(new[] { 4, 11 }), "response pool includes enabled preferred phrases");
            var preferred = new PhrasePool(new[] { 3, 4, 8 }, true, new Random(4));
            for (int i = 0; i < 20; i++) Check(preferred.Next() == 4, "sun preferred pool respects filtering");
            var fallback = new PhrasePool(new[] { 3, 8 }, true, new Random(4));
            var fallbackSeen = new HashSet<int>();
            for (int i = 0; i < 2; i++) fallbackSeen.Add(fallback.Next());
            Check(fallbackSeen.SetEquals(new[] { 3, 8 }), "empty preferred pool falls back to enabled pool");
            Console.WriteLine("PASS: " + checks + " assertions; 100 rounds each for 28/37/11/8/1 pools.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); return 1; }
    }
}
