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
    /// Delivery is synchronous on the calling thread, matching the type argument exactly (not the message's runtime type:
    /// <c>Send&lt;object&gt;("x")</c> reaches <c>object</c> handlers, not <c>string</c> ones), to a snapshot of the
    /// live registrations taken when <see cref="Send{TMessage}"/> is invoked (so handlers may register or
    /// unregister during delivery). An exception thrown by a handler propagates to the caller and stops
    /// delivery to the remaining handlers. Do not capture the recipient in a <em>different</em> long-lived
    /// object (a static field, another recipient's handler) — that reference, not the messenger, would keep
    /// it alive.
    /// </para>
    /// </summary>
    public sealed class Messenger
    {
        // recipient -> its registration. Weakly keyed: an entry vanishes when its recipient is collected.
        private readonly ConditionalWeakTable<object, Registration> _registrations = new ConditionalWeakTable<object, Registration>();
        // message type -> the registrations that have (or had) handlers for it, held weakly and pruned as they go stale.
        // Send walked every recipient of every message type, under the lock, to find the few for one type.
        private readonly Dictionary<Type, TypeIndex> _byType = new Dictionary<Type, TypeIndex>();
        private readonly object _gate = new object();

        private sealed class Registration
        {
            public readonly Dictionary<Type, List<Delegate>> Handlers = new Dictionary<Type, List<Delegate>>();
            // The types whose index holds this registration, so registering again does not add it twice.
            public readonly HashSet<Type> Indexed = new HashSet<Type>();
            public bool Removed;
        }

        private sealed class TypeIndex
        {
            public readonly List<WeakReference<Registration>> Entries = new List<WeakReference<Registration>>();
            public int PruneAt = 16;
        }

        /// <summary>A shared default instance for app-wide messaging.</summary>
        public static Messenger Default { get; } = new Messenger();

        /// <summary>
        /// Registers <paramref name="handler"/> to receive messages of type <typeparamref name="TMessage"/> for as
        /// long as <paramref name="recipient"/> is alive. A recipient may register several handlers, for the same
        /// or different message types. The recipient must be a reference type.
        /// </summary>
        public void Register<TMessage>(object recipient, Action<TMessage> handler)
        {
            if (recipient is null) throw new ArgumentNullException(nameof(recipient));
            if (recipient.GetType().IsValueType)   // each call boxes it afresh: the registration could never be found again
                throw new ArgumentException("The recipient must be a reference type; a boxed value has no identity to register.", nameof(recipient));
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            Type type = typeof(TMessage);
            lock (_gate)
            {
                Registration registration = _registrations.GetValue(recipient, _ => new Registration());
                if (!registration.Handlers.TryGetValue(type, out var list))
                {
                    list = new List<Delegate>();
                    registration.Handlers[type] = list;
                }
                list.Add(handler);
                if (registration.Indexed.Add(type))
                {
                    if (!_byType.TryGetValue(type, out var index)) _byType[type] = index = new TypeIndex();
                    index.Entries.Add(new WeakReference<Registration>(registration));
                    // Pruned as it doubles, so recipients collected without ever being sent to don't pile up.
                    if (index.Entries.Count >= index.PruneAt)
                    {
                        Walk(type, index, null);
                        index.PruneAt = Math.Max(16, index.Entries.Count * 2);
                    }
                }
            }
        }

        /// <summary>Removes every handler <paramref name="recipient"/> registered for <typeparamref name="TMessage"/>. Returns true if any were removed.</summary>
        public bool Unregister<TMessage>(object recipient)
        {
            if (recipient is null) throw new ArgumentNullException(nameof(recipient));
            lock (_gate)
            {
                if (!_registrations.TryGetValue(recipient, out var registration) || !registration.Handlers.Remove(typeof(TMessage)))
                    return false;
                if (registration.Handlers.Count == 0)
                {
                    _registrations.Remove(recipient);
                    registration.Removed = true;
                }
                // Dropped from the type's index now, so a re-registration always goes last: the stale entry kept its old
                // slot until a Send pruned it, so the order depended on whether one had run in between.
                else if (registration.Indexed.Remove(typeof(TMessage)) && _byType.TryGetValue(typeof(TMessage), out var index))
                    index.Entries.RemoveAll(w => w.TryGetTarget(out var r) && ReferenceEquals(r, registration));
                return true;
            }
        }

        /// <summary>Removes every registration of <paramref name="recipient"/>, for all message types. Returns true if it had any.</summary>
        public bool UnregisterAll(object recipient)
        {
            if (recipient is null) throw new ArgumentNullException(nameof(recipient));
            lock (_gate)
            {
                if (!_registrations.TryGetValue(recipient, out var registration)) return false;
                _registrations.Remove(recipient);
                registration.Removed = true;
                return true;
            }
        }

        /// <summary>True if <paramref name="recipient"/> currently has a handler registered for <typeparamref name="TMessage"/>.</summary>
        public bool IsRegistered<TMessage>(object recipient)
        {
            if (recipient is null) throw new ArgumentNullException(nameof(recipient));
            lock (_gate)
                return _registrations.TryGetValue(recipient, out var registration) && registration.Handlers.ContainsKey(typeof(TMessage));
        }

        /// <summary>
        /// Delivers <paramref name="message"/> to every live recipient's handlers for <typeparamref name="TMessage"/>
        /// exactly — the type argument, not the message's runtime type — in the order the recipients first registered for
        /// it. The cost is in proportion to that type's recipients, not every recipient's.
        /// </summary>
        public void Send<TMessage>(TMessage message)
        {
            var handlers = new List<Delegate>();
            lock (_gate)
                if (_byType.TryGetValue(typeof(TMessage), out var index))
                    Walk(typeof(TMessage), index, handlers);
            foreach (var handler in handlers)
                ((Action<TMessage>)handler)(message);
        }

        // Drops the index's stale entries (recipient collected, unregistered, or no longer handling the type), collecting the
        // live entries' handlers when asked.
        private void Walk(Type type, TypeIndex index, List<Delegate>? handlers)
        {
            List<WeakReference<Registration>> entries = index.Entries;
            int kept = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                WeakReference<Registration> entry = entries[i];
                if (!entry.TryGetTarget(out Registration? registration) || registration.Removed) continue;
                if (!registration.Handlers.TryGetValue(type, out var list))
                {
                    registration.Indexed.Remove(type);
                    continue;
                }
                entries[kept++] = entry;
                handlers?.AddRange(list);
            }
            entries.RemoveRange(kept, entries.Count - kept);
            if (kept == 0) _byType.Remove(type);
        }

        /// <summary>Removes all registrations.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                foreach (var pair in _registrations) pair.Value.Removed = true;
                _registrations.Clear();
                _byType.Clear();
            }
        }
    }
}
