// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// A thread-safe, <b>weak-reference</b> message aggregator (publish/subscribe) for decoupling view models.
    /// A recipient registers a handler for a message type and publishers <see cref="Send{TMessage}"/>
    /// instances of it. Registrations are keyed on the recipient object and held weakly, so a view model that
    /// forgets to unregister is still garbage-collected and its handlers simply stop being invoked — the
    /// strong-reference leak that hand-rolled event aggregators reliably cause cannot happen here.
    /// <para>
    /// The handler may freely capture the recipient (e.g. a lambda using <c>this</c>): registrations live in a
    /// <see cref="ConditionalWeakTable{TKey, TValue}"/>, whose entries do not keep their key alive even when the
    /// value refers back to it. Unlike weakly referencing the handler delegate itself, this also means an
    /// inline lambda is NOT collected out from under a live recipient — a registration lasts exactly as long
    /// as its recipient (or until <see cref="Unregister{TMessage}"/>/<see cref="UnregisterAll"/>).
    /// </para>
    /// <para>
    /// Delivery is synchronous on the calling thread, matching on the exact message type, to a snapshot of the
    /// live registrations taken when <see cref="Send{TMessage}"/> is invoked (so handlers may register or
    /// unregister during delivery). An exception thrown by a handler propagates to the caller and stops
    /// delivery to the remaining handlers. Do not capture the recipient in a <em>different</em> long-lived
    /// object (a static field, another recipient's handler) — that reference, not the messenger, would keep
    /// it alive.
    /// </para>
    /// </summary>
    public sealed class Messenger
    {
        // recipient -> (message type -> handlers). Weakly keyed: an entry vanishes when its recipient is collected.
        private readonly ConditionalWeakTable<object, Dictionary<Type, List<Delegate>>> _registrations =
            new ConditionalWeakTable<object, Dictionary<Type, List<Delegate>>>();
        private readonly object _gate = new object();

        /// <summary>A shared default instance for app-wide messaging.</summary>
        public static Messenger Default { get; } = new Messenger();

        /// <summary>
        /// Registers <paramref name="handler"/> to receive messages of type <typeparamref name="TMessage"/> for as
        /// long as <paramref name="recipient"/> is alive. A recipient may register several handlers, for the same
        /// or different message types.
        /// </summary>
        public void Register<TMessage>(object recipient, Action<TMessage> handler)
        {
            if (recipient is null) throw new ArgumentNullException(nameof(recipient));
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            lock (_gate)
            {
                var byType = _registrations.GetValue(recipient, _ => new Dictionary<Type, List<Delegate>>());
                if (!byType.TryGetValue(typeof(TMessage), out var list))
                {
                    list = new List<Delegate>();
                    byType[typeof(TMessage)] = list;
                }
                list.Add(handler);
            }
        }

        /// <summary>Removes every handler <paramref name="recipient"/> registered for <typeparamref name="TMessage"/>. Returns true if any were removed.</summary>
        public bool Unregister<TMessage>(object recipient)
        {
            if (recipient is null) throw new ArgumentNullException(nameof(recipient));
            lock (_gate)
            {
                if (!_registrations.TryGetValue(recipient, out var byType) || !byType.Remove(typeof(TMessage)))
                    return false;
                if (byType.Count == 0)
                    _registrations.Remove(recipient);
                return true;
            }
        }

        /// <summary>Removes every registration of <paramref name="recipient"/>, for all message types. Returns true if it had any.</summary>
        public bool UnregisterAll(object recipient)
        {
            if (recipient is null) throw new ArgumentNullException(nameof(recipient));
            lock (_gate)
                return _registrations.Remove(recipient);
        }

        /// <summary>True if <paramref name="recipient"/> currently has a handler registered for <typeparamref name="TMessage"/>.</summary>
        public bool IsRegistered<TMessage>(object recipient)
        {
            if (recipient is null) throw new ArgumentNullException(nameof(recipient));
            lock (_gate)
                return _registrations.TryGetValue(recipient, out var byType) && byType.ContainsKey(typeof(TMessage));
        }

        /// <summary>Delivers <paramref name="message"/> to every live recipient's handlers for its exact type.</summary>
        public void Send<TMessage>(TMessage message)
        {
            var handlers = new List<Action<TMessage>>();
            lock (_gate)
            {
                // Enumerating yields only live recipients; the snapshot keeps them alive for the delivery.
                foreach (var pair in _registrations)
                    if (pair.Value.TryGetValue(typeof(TMessage), out var list))
                        foreach (var d in list)
                            handlers.Add((Action<TMessage>)d);
            }
            foreach (var handler in handlers)
                handler(message);
        }

        /// <summary>Removes all registrations.</summary>
        public void Clear()
        {
            lock (_gate)
                _registrations.Clear();
        }
    }
}
