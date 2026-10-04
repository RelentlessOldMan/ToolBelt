using System;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class MessengerTests
    {
        private sealed class Ping { public int Value; }
        private sealed class Pong { }

        public void Send_DeliversToSubscribersOfThatType()
        {
            var m = new Messenger();
            int sum = 0;
            Action<Ping> handler = p => sum += p.Value;
            m.Subscribe(handler);

            m.Send(new Ping { Value = 3 });
            m.Send(new Ping { Value = 4 });
            Check.Equal(7, sum);

            // A different message type is not delivered to Ping subscribers.
            m.Send(new Pong());
            Check.Equal(7, sum);
        }

        public void MultipleSubscribers_AllReceive()
        {
            var m = new Messenger();
            int a = 0, b = 0;
            m.Subscribe<Ping>(p => a += p.Value);
            m.Subscribe<Ping>(p => b += p.Value * 2);
            m.Send(new Ping { Value = 5 });
            Check.Equal(5, a);
            Check.Equal(10, b);
        }

        public void Unsubscribe_StopsDelivery()
        {
            var m = new Messenger();
            int count = 0;
            Action<Ping> handler = _ => count++;
            m.Subscribe(handler);

            m.Send(new Ping());
            Check.Equal(1, count);

            Check.True(m.Unsubscribe(handler), "removed");
            m.Send(new Ping());
            Check.Equal(1, count); // no further delivery

            Check.False(m.Unsubscribe(handler), "second remove returns false");
        }

        public void Clear_RemovesAll()
        {
            var m = new Messenger();
            int count = 0;
            m.Subscribe<Ping>(_ => count++);
            m.Clear();
            m.Send(new Ping());
            Check.Equal(0, count);
        }

        public void Send_WithNoSubscribers_IsNoOp()
        {
            var m = new Messenger();
            m.Send(new Ping { Value = 1 }); // must not throw
        }

        public void Validation_Throws()
        {
            var m = new Messenger();
            Check.Throws<ArgumentNullException>(() => m.Subscribe<Ping>(null!));
            Check.Throws<ArgumentNullException>(() => m.Unsubscribe<Ping>(null!));
        }
    }
}
