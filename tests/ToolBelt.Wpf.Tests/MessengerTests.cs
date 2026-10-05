using System;
using System.Runtime.CompilerServices;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class MessengerTests
    {
        private sealed class Ping { public int Value; }
        private sealed class Pong { }
        private sealed class Recipient { public int Hits; }
        private sealed class Counter { public int Count; }

        public void Send_DeliversToRecipientsOfThatType()
        {
            var m = new Messenger();
            var r = new Recipient();
            m.Register<Ping>(r, p => r.Hits += p.Value);

            m.Send(new Ping { Value = 3 });
            m.Send(new Ping { Value = 4 });
            Check.Equal(7, r.Hits);

            // A different message type is not delivered to Ping handlers.
            m.Send(new Pong());
            Check.Equal(7, r.Hits);
        }

        public void MultipleRecipientsAndHandlers_AllReceive()
        {
            var m = new Messenger();
            var a = new Recipient();
            var b = new Recipient();
            m.Register<Ping>(a, p => a.Hits += p.Value);
            m.Register<Ping>(b, p => b.Hits += p.Value * 2);
            m.Register<Ping>(b, p => b.Hits += 100); // a second handler on the same recipient
            m.Send(new Ping { Value = 5 });
            Check.Equal(5, a.Hits);
            Check.Equal(110, b.Hits);
        }

        public void Unregister_StopsDeliveryForThatTypeOnly()
        {
            var m = new Messenger();
            var r = new Recipient();
            int pongs = 0;
            m.Register<Ping>(r, _ => r.Hits++);
            m.Register<Pong>(r, _ => pongs++);

            Check.True(m.Unregister<Ping>(r), "removed");
            Check.False(m.IsRegistered<Ping>(r));
            Check.True(m.IsRegistered<Pong>(r), "other type untouched");

            m.Send(new Ping());
            m.Send(new Pong());
            Check.Equal(0, r.Hits);
            Check.Equal(1, pongs);

            Check.False(m.Unregister<Ping>(r), "second remove returns false");
        }

        public void UnregisterAll_RemovesEveryType()
        {
            var m = new Messenger();
            var r = new Recipient();
            m.Register<Ping>(r, _ => r.Hits++);
            m.Register<Pong>(r, _ => r.Hits++);
            Check.True(m.UnregisterAll(r));
            m.Send(new Ping());
            m.Send(new Pong());
            Check.Equal(0, r.Hits);
            Check.False(m.UnregisterAll(r));
        }

        public void Clear_RemovesAll()
        {
            var m = new Messenger();
            var r = new Recipient();
            m.Register<Ping>(r, _ => r.Hits++);
            m.Clear();
            m.Send(new Ping());
            Check.Equal(0, r.Hits);
        }

        public void Send_WithNoRecipients_IsNoOp()
        {
            var m = new Messenger();
            m.Send(new Ping { Value = 1 }); // must not throw
        }

        public void HandlerMayUnregisterDuringDelivery()
        {
            var m = new Messenger();
            var r = new Recipient();
            m.Register<Ping>(r, _ => { r.Hits++; m.UnregisterAll(r); });
            m.Send(new Ping());
            m.Send(new Ping());
            Check.Equal(1, r.Hits);
        }

        // ---------- the weak-reference guarantees ----------

        public void AbandonedRecipient_IsCollected_AndStopsReceiving()
        {
            var m = new Messenger();
            var counter = new Counter();
            WeakReference weak = RegisterAbandonedRecipient(m, counter);

            m.Send(new Ping()); // may or may not reach it before collection — reset after GC
            ForceFullGc();
            counter.Count = 0;

            Check.False(weak.IsAlive, "the messenger must not keep an unregistered recipient alive");
            m.Send(new Ping());
            Check.Equal(0, counter.Count);
        }

        public void LiveRecipient_InlineLambda_SurvivesGc()
        {
            // The classic weak-delegate bug: an inline lambda is only referenced by the messenger and gets
            // collected, silently ending delivery. Registrations here live as long as the recipient.
            var m = new Messenger();
            var r = new Recipient();
            m.Register<Ping>(r, p => r.Hits += p.Value);

            ForceFullGc();
            m.Send(new Ping { Value = 9 });
            Check.Equal(9, r.Hits);
            GC.KeepAlive(r);
        }

        public void Validation_Throws()
        {
            var m = new Messenger();
            var r = new Recipient();
            Check.Throws<ArgumentNullException>(() => m.Register<Ping>(null!, _ => { }));
            Check.Throws<ArgumentNullException>(() => m.Register<Ping>(r, null!));
            Check.Throws<ArgumentNullException>(() => m.Unregister<Ping>(null!));
            Check.Throws<ArgumentNullException>(() => m.UnregisterAll(null!));
            Check.Throws<ArgumentNullException>(() => m.IsRegistered<Ping>(null!));
        }

        // Separate, non-inlined frame so no local in the test method roots the recipient.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RegisterAbandonedRecipient(Messenger m, Counter counter)
        {
            var recipient = new Recipient();
            // The handler deliberately captures the recipient — that must not keep it alive.
            m.Register<Ping>(recipient, _ => { recipient.Hits++; counter.Count++; });
            return new WeakReference(recipient);
        }

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
