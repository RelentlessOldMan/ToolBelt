// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// A minimal, thread-safe message aggregator (publish/subscribe) for decoupling view models: subscribers
    /// register a handler for a message type and publishers <see cref="Send{TMessage}"/> instances of it.
    /// Handlers are held with <b>strong</b> references, so a subscriber must <see cref="Unsubscribe{TMessage}"/>
    /// (or call <see cref="Clear"/>) to avoid keeping itself alive — this matters most for the process-wide
    /// <see cref="Default"/> instance, where a forgotten subscriber leaks for the life of the process.
    /// Delivery is synchronous on the calling thread, to a snapshot of the subscribers taken when
    /// <see cref="Send{TMessage}"/> is invoked; an exception thrown by one handler propagates to the caller
    /// and stops delivery to the remaining handlers.
    /// </summary>
    public sealed class Messenger
    {
        private readonly Dictionary<Type, List<Delegate>> _subscribers = new Dictionary<Type, List<Delegate>>();
        private readonly object _gate = new object();

        /// <summary>A shared default instance for app-wide messaging.</summary>
        public static Messenger Default { get; } = new Messenger();

        /// <summary>Registers <paramref name="handler"/> to receive messages of type <typeparamref name="TMessage"/>.</summary>
        public void Subscribe<TMessage>(Action<TMessage> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            lock (_gate)
            {
                if (!_subscribers.TryGetValue(typeof(TMessage), out var list))
                {
                    list = new List<Delegate>();
                    _subscribers[typeof(TMessage)] = list;
                }
                list.Add(handler);
            }
        }

        /// <summary>Removes a previously-registered handler. Returns true if it was found and removed.</summary>
        public bool Unsubscribe<TMessage>(Action<TMessage> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            lock (_gate)
            {
                if (!_subscribers.TryGetValue(typeof(TMessage), out var list))
                    return false;
                bool removed = list.Remove(handler);
                if (list.Count == 0)
                    _subscribers.Remove(typeof(TMessage));
                return removed;
            }
        }

        /// <summary>Delivers <paramref name="message"/> to every current subscriber for its type.</summary>
        public void Send<TMessage>(TMessage message)
        {
            Delegate[] handlers;
            lock (_gate)
            {
                if (!_subscribers.TryGetValue(typeof(TMessage), out var list))
                    return;
                handlers = list.ToArray(); // snapshot so handlers may (un)subscribe during delivery
            }
            foreach (var handler in handlers)
                ((Action<TMessage>)handler)(message);
        }

        /// <summary>Removes all subscribers.</summary>
        public void Clear()
        {
            lock (_gate)
                _subscribers.Clear();
        }
    }
}
