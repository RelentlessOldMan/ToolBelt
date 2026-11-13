// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// Parses and formats menu shortcuts such as <c>"Ctrl+Shift+S"</c> or <c>"F5"</c>. Accepts what a menu item's
    /// <see cref="ToolStripMenuItem.ShortcutKeys"/> accepts (<see cref="ToolStripManager.IsValidShortcut"/>) — a key with
    /// Ctrl or Alt, or a function key, Insert or Delete on its own or with any modifiers — except mouse buttons
    /// (LButton, RButton, MButton, XButton1/2) and the members that are no key at all (the KeyCode mask, Packet, NoName,
    /// ProcessKey), which WinForms takes but no keyboard can press.
    /// </summary>
    public static class MenuShortcut
    {
        /// <summary>
        /// Parses modifiers (Ctrl/Control, Alt, Shift) and one key ("S", "F5", "Delete", "1", "Plus", "PageDown", "/", "-").
        /// "+" and "-" also name the Plus and Minus keys ("Ctrl++", "Ctrl+-"); <see cref="Format"/> writes "Plus" and "Minus".
        /// Throws <see cref="FormatException"/> for anything a menu item would reject.
        /// </summary>
        public static Keys Parse(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (!TryParse(text, out Keys keys)) throw new FormatException($"'{text}' is not a valid shortcut.");
            return keys;
        }

        public static bool TryParse(string? text, out Keys keys)
        {
            keys = Keys.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text!.Split('+');
            // A trailing "+" names the Plus key: "Ctrl++".
            if (parts.Length >= 2 && parts[parts.Length - 1].Trim().Length == 0 && parts[parts.Length - 2].Trim().Length == 0)
                parts = ReplaceTail(parts, "Oemplus");
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": keys |= Keys.Control; break;
                    case "alt": keys |= Keys.Alt; break;
                    case "shift": keys |= Keys.Shift; break;
                    default: keys = Keys.None; return false;
                }
            }
            string k = parts[parts.Length - 1].Trim();
            Keys key = Keys.None;
            if (k.Length == 1 && k[0] >= '0' && k[0] <= '9') key = Keys.D0 + (k[0] - '0');
            else if (k.Equals("Del", StringComparison.OrdinalIgnoreCase)) key = Keys.Delete;
            else if (k.Equals("Esc", StringComparison.OrdinalIgnoreCase)) key = Keys.Escape;
            else if (k == "-") key = Keys.OemMinus;
            else
            {
                foreach (var (fk, name) in Friendly)
                    if (k.Equals(name, StringComparison.OrdinalIgnoreCase)) { key = fk; break; }
                // A single enum name only: Enum.TryParse would also take "A,B" (OR-ed) and numbers.
                if (key == Keys.None && (!IsName(k) || !Enum.TryParse(k, ignoreCase: true, out key) || !Enum.IsDefined(key)))
                    key = Keys.None;
            }
            if (key == Keys.None || (key & Keys.Modifiers) != 0 || IsMouseButton(key) || NotAKey(key) || !ToolStripManager.IsValidShortcut(keys | key))
            {
                keys = Keys.None;
                return false;
            }
            keys |= key;
            return true;
        }

        // Display names for keys whose enum name is an alias or a code ("Next", "Oem2"): Format writes them, TryParse reads them.
        private static readonly (Keys Key, string Name)[] Friendly =
        {
            (Keys.PageUp, "PageUp"), (Keys.PageDown, "PageDown"), (Keys.Enter, "Enter"), (Keys.CapsLock, "CapsLock"),
            (Keys.Back, "Backspace"), (Keys.Oemplus, "Plus"), (Keys.OemMinus, "Minus"), (Keys.OemQuestion, "/"),
            (Keys.Oemcomma, ","), (Keys.OemPeriod, "."), (Keys.OemSemicolon, ";"), (Keys.OemQuotes, "'"),
            (Keys.OemOpenBrackets, "["), (Keys.OemCloseBrackets, "]"), (Keys.OemPipe, "\\"), (Keys.Oemtilde, "`"),
        };

        private static bool IsName(string s)
        {
            if (s.Length == 0 || !char.IsLetter(s[0])) return false;
            foreach (char c in s)
                if (!char.IsLetterOrDigit(c)) return false;
            return true;
        }

        private static bool IsMouseButton(Keys key)
            => key == Keys.LButton || key == Keys.RButton || key == Keys.MButton || key == Keys.XButton1 || key == Keys.XButton2;

        // Defined Keys members no keystroke produces: the mask of every key code, and codes Windows uses internally.
        private static bool NotAKey(Keys key)
            => key == Keys.KeyCode || key == Keys.Packet || key == Keys.NoName || key == Keys.ProcessKey;

        /// <summary>"Ctrl+Shift+S" style text for display.</summary>
        public static string Format(Keys keys)
        {
            var parts = new List<string>();
            if ((keys & Keys.Control) != 0) parts.Add("Ctrl");
            if ((keys & Keys.Alt) != 0) parts.Add("Alt");
            if ((keys & Keys.Shift) != 0) parts.Add("Shift");
            Keys key = keys & Keys.KeyCode;
            string? text = null;
            foreach (var (fk, name) in Friendly)
                if (fk == key) { text = name; break; }
            parts.Add(text ?? (key >= Keys.D0 && key <= Keys.D9 ? ((int)(key - Keys.D0)).ToString(CultureInfo.InvariantCulture) : key.ToString()));
            return string.Join("+", parts);
        }

        private static string[] ReplaceTail(string[] parts, string key)
        {
            var list = new List<string>(parts);
            list.RemoveAt(list.Count - 1);
            list[list.Count - 1] = key;
            return list.ToArray();
        }
    }

    /// <summary>
    /// Adds items to a menu or drop-down: actions, <see cref="ICommand"/>s (Enabled follows <c>CanExecute</c>), check
    /// items, separators and submenus. Item <c>Name</c>s default to the text without mnemonics, so items can be found
    /// with <see cref="MenuBuilder.FindItem"/>.
    /// </summary>
    public sealed class MenuItemsBuilder
    {
        private readonly ToolStripItemCollection _items;

        internal MenuItemsBuilder(ToolStripItemCollection items) => _items = items;

        /// <summary>An item that runs <paramref name="onClick"/>.</summary>
        public MenuItemsBuilder Item(string text, Action onClick, string? shortcut = null, Image? image = null, string? name = null)
        {
            if (onClick is null) throw new ArgumentNullException(nameof(onClick));
            var item = Create(text, shortcut, image, name);
            item.Click += (_, __) => onClick();
            _items.Add(item);
            return this;
        }

        /// <summary>
        /// An item bound to <paramref name="command"/>: clicking executes it with <paramref name="parameter"/>, and Enabled
        /// tracks CanExecute. The optional arguments come in the same order as the <see cref="Action"/> overload's.
        /// </summary>
        public MenuItemsBuilder Item(string text, ICommand command, string? shortcut = null, Image? image = null, string? name = null, object? parameter = null)
        {
            var item = Create(text, shortcut, image, name);
            CommandBinding.Bind(item, command, parameter);
            _items.Add(item);
            return this;
        }

        /// <summary>A check item; <paramref name="onChanged"/> receives the new state after each change (a click, or setting Checked).</summary>
        public MenuItemsBuilder Check(string text, bool isChecked, Action<bool> onChanged, string? shortcut = null, string? name = null)
        {
            if (onChanged is null) throw new ArgumentNullException(nameof(onChanged));
            var item = Create(text, shortcut, null, name);
            item.CheckOnClick = true;
            item.Checked = isChecked;
            item.CheckedChanged += (_, __) => onChanged(item.Checked);
            _items.Add(item);
            return this;
        }

        public MenuItemsBuilder Separator()
        {
            _items.Add(new ToolStripSeparator());
            return this;
        }

        public MenuItemsBuilder Submenu(string text, Action<MenuItemsBuilder> items, string? name = null)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            var menu = Create(text, null, null, name);
            items(new MenuItemsBuilder(menu.DropDownItems));
            _items.Add(menu);
            return this;
        }

        private static ToolStripMenuItem Create(string text, string? shortcut, Image? image, string? name)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            var item = new ToolStripMenuItem(text) { Name = name ?? MenuBuilder.PlainText(text), Image = image };
            if (shortcut != null)
            {
                item.ShortcutKeys = MenuShortcut.Parse(shortcut);
                item.ShortcutKeyDisplayString = MenuShortcut.Format(item.ShortcutKeys);
            }
            return item;
        }
    }

    /// <summary>
    /// Builds a <see cref="MenuStrip"/> from a declarative description instead of designer code:
    /// <code>
    /// var menu = new MenuBuilder()
    ///     .Menu("&amp;File", m => m.Item("&amp;Open...", OpenFile, "Ctrl+O").Separator().Item("E&amp;xit", Close))
    ///     .Menu("&amp;View", m => m.Check("&amp;Grid", true, on => ShowGrid(on)))
    ///     .Build();
    /// </code>
    /// The result is ordinary WinForms items; <see cref="FindItem"/> locates one by its text path (e.g.
    /// <c>"File/Open..."</c>) so tests can <c>PerformClick()</c> it without a UI.
    /// </summary>
    public sealed class MenuBuilder
    {
        private readonly List<(string Text, Action<MenuItemsBuilder> Items, string? Name)> _menus = new List<(string, Action<MenuItemsBuilder>, string?)>();

        public MenuBuilder Menu(string text, Action<MenuItemsBuilder> items, string? name = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (items is null) throw new ArgumentNullException(nameof(items));
            _menus.Add((text, items, name));
            return this;
        }

        public MenuStrip Build()
        {
            var strip = new MenuStrip();
            ApplyTo(strip);
            return strip;
        }

        /// <summary>Appends the described menus to an existing strip.</summary>
        public void ApplyTo(MenuStrip strip)
        {
            if (strip is null) throw new ArgumentNullException(nameof(strip));
            foreach (var (text, items, name) in _menus)
            {
                var top = new ToolStripMenuItem(text) { Name = name ?? PlainText(text) };
                items(new MenuItemsBuilder(top.DropDownItems));
                strip.Items.Add(top);
            }
        }

        /// <summary>
        /// Finds an item by its path of texts, ignoring mnemonics (in the item's text and in the path, so <c>"&amp;File/&amp;Open"</c>
        /// works too), surrounding spaces and case, e.g. <c>"File/Recent/report.csv"</c>; null if absent.
        /// Separators are never matched, and an item whose text contains "/" cannot be named.
        /// </summary>
        public static ToolStripItem? FindItem(ToolStrip strip, string path)
        {
            if (strip is null) throw new ArgumentNullException(nameof(strip));
            if (path is null) throw new ArgumentNullException(nameof(path));
            ToolStripItemCollection items = strip.Items;
            ToolStripItem? found = null;
            foreach (string segment in path.Split('/'))
            {
                found = null;
                foreach (ToolStripItem candidate in items)
                    if (candidate is not ToolStripSeparator && string.Equals(PlainText(candidate.Text ?? "").Trim(), PlainText(segment).Trim(), StringComparison.OrdinalIgnoreCase)) { found = candidate; break; }
                if (found is null) return null;
                items = found is ToolStripDropDownItem dd ? dd.DropDownItems : new ToolStripItemCollection(strip, Array.Empty<ToolStripItem>());
            }
            return found;
        }

        /// <summary>Text with WinForms mnemonics removed ("&amp;Save" → "Save", "R&amp;&amp;D" → "R&amp;D").</summary>
        public static string PlainText(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '&')
                {
                    if (i + 1 < text.Length && text[i + 1] == '&') { sb.Append('&'); i++; }
                    continue;
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Builds a <see cref="ToolStrip"/> (toolbar) declaratively: buttons for actions or <see cref="ICommand"/>s, toggle
    /// buttons, labels, separators and drop-down buttons. Text doubles as the tooltip unless one is given. Like
    /// <see cref="MenuBuilder"/>, each <see cref="Build"/> or <see cref="ApplyTo"/> creates fresh items (an item belongs to
    /// one strip), so one builder can fill several toolbars.
    /// </summary>
    public sealed class ToolStripBuilder
    {
        private readonly List<Func<ToolStripItem>> _items = new List<Func<ToolStripItem>>();

        public ToolStripBuilder Button(string text, Action onClick, Image? image = null, string? toolTip = null, string? name = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (onClick is null) throw new ArgumentNullException(nameof(onClick));
            _items.Add(() =>
            {
                var b = CreateButton(text, image, toolTip, name);
                b.Click += (_, __) => onClick();
                return b;
            });
            return this;
        }

        /// <summary>
        /// A button bound to <paramref name="command"/> (executed with <paramref name="parameter"/>; Enabled tracks
        /// CanExecute). The optional arguments come in the same order as the <see cref="Action"/> overload's.
        /// </summary>
        public ToolStripBuilder Button(string text, ICommand command, Image? image = null, string? toolTip = null, string? name = null, object? parameter = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (command is null) throw new ArgumentNullException(nameof(command));
            _items.Add(() =>
            {
                var b = CreateButton(text, image, toolTip, name);
                CommandBinding.Bind(b, command, parameter);
                return b;
            });
            return this;
        }

        public ToolStripBuilder Toggle(string text, bool isChecked, Action<bool> onChanged, Image? image = null, string? toolTip = null, string? name = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (onChanged is null) throw new ArgumentNullException(nameof(onChanged));
            _items.Add(() =>
            {
                var b = CreateButton(text, image, toolTip, name);
                b.CheckOnClick = true;
                b.Checked = isChecked;
                b.CheckedChanged += (_, __) => onChanged(b.Checked);
                return b;
            });
            return this;
        }

        public ToolStripBuilder Label(string text, string? name = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            _items.Add(() => new ToolStripLabel(text) { Name = name ?? MenuBuilder.PlainText(text) });
            return this;
        }

        public ToolStripBuilder Separator()
        {
            _items.Add(() => new ToolStripSeparator());
            return this;
        }

        public ToolStripBuilder DropDown(string text, Action<MenuItemsBuilder> items, Image? image = null, string? name = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (items is null) throw new ArgumentNullException(nameof(items));
            _items.Add(() =>
            {
                var dd = new ToolStripDropDownButton(text, image) { Name = name ?? MenuBuilder.PlainText(text) };
                items(new MenuItemsBuilder(dd.DropDownItems));
                return dd;
            });
            return this;
        }

        public ToolStrip Build()
        {
            var strip = new ToolStrip();
            ApplyTo(strip);
            return strip;
        }

        /// <summary>Appends the described items, newly created, to an existing strip.</summary>
        public void ApplyTo(ToolStrip strip)
        {
            if (strip is null) throw new ArgumentNullException(nameof(strip));
            var created = new ToolStripItem[_items.Count];
            for (int i = 0; i < created.Length; i++) created[i] = _items[i]();
            strip.Items.AddRange(created);
        }

        private static ToolStripButton CreateButton(string text, Image? image, string? toolTip, string? name)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return new ToolStripButton(text, image)
            {
                Name = name ?? MenuBuilder.PlainText(text),
                ToolTipText = toolTip ?? MenuBuilder.PlainText(text),
                DisplayStyle = image is null ? ToolStripItemDisplayStyle.Text : ToolStripItemDisplayStyle.Image,
            };
        }
    }

    /// <summary>
    /// Wires a <see cref="ToolStripItem"/> to an <see cref="ICommand"/>. The command holds the item only weakly (a
    /// long-lived view-model command must not keep a closed but undisposed dialog's menu alive) and is unhooked when the
    /// item is disposed or collected. A CanExecuteChanged raised on another thread (a view-model updating from a worker)
    /// is marshalled to the item's UI thread, so Enabled and its EnabledChanged handlers never run off it. The parameter
    /// is held through the item too, so an item dropped without being disposed (a context menu rebuilt on each opening,
    /// its old items cleared) does not keep its parameter alive from the command; the leftover subscriptions of such
    /// items are removed the next time the command is bound or raises CanExecuteChanged.
    /// </summary>
    internal static class CommandBinding
    {
        // Each command's subscriptions made here, so dead ones can be pruned even when the command never raises the event.
        private static readonly ConditionalWeakTable<ICommand, List<WeakSync>> Subscriptions = new ConditionalWeakTable<ICommand, List<WeakSync>>();

        public static void Bind(ToolStripItem item, ICommand command, object? parameter)
        {
            if (command is null) throw new ArgumentNullException(nameof(command));
            item.Enabled = command.CanExecute(parameter); // before hooking: a throwing CanExecute leaves nothing subscribed
            var target = new Target(parameter);
            item.Click += (_, __) =>
            {
                if (command.CanExecute(target.Parameter)) command.Execute(target.Parameter);
            };
            // Before the strip has a handle, the binding thread's context is the only way back to the UI thread. A UI
            // (STA) thread that has not yet created a control has none installed, so make one for it.
            var context = SynchronizationContext.Current as WindowsFormsSynchronizationContext;
            if (context is null && Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                context = new WindowsFormsSynchronizationContext();
            var sync = new WeakSync(item, target, command, context);
            List<WeakSync> list = Subscriptions.GetValue(command, _ => new List<WeakSync>());
            lock (list)
            {
                list.RemoveAll(s => { if (s.IsAlive) return false; command.CanExecuteChanged -= s.Handler; return true; });
                list.Add(sync);
            }
            command.CanExecuteChanged += sync.Handler;
            item.Disposed += (_, __) => Unsubscribe(command, sync);
        }

        private static void Unsubscribe(ICommand command, WeakSync sync)
        {
            command.CanExecuteChanged -= sync.Handler;
            if (Subscriptions.TryGetValue(command, out List<WeakSync>? list))
                lock (list) list.Remove(sync);
        }

        // The parameter, reachable from the item (its Click handler) and only weakly from the command.
        private sealed class Target
        {
            public readonly object? Parameter;
            public Target(object? parameter) => Parameter = parameter;
        }

        private sealed class WeakSync
        {
            private readonly WeakReference<ToolStripItem> _item;
            private readonly WeakReference<Target> _target;
            private readonly ICommand _command;
            private readonly SynchronizationContext? _context; // the binding thread's WinForms context, if it had one
            private readonly int _thread;

            public WeakSync(ToolStripItem item, Target target, ICommand command, SynchronizationContext? context)
            {
                _item = new WeakReference<ToolStripItem>(item);
                _target = new WeakReference<Target>(target);
                _command = command;
                _context = context;
                _thread = Environment.CurrentManagedThreadId;
            }

            public bool IsAlive => _item.TryGetTarget(out _) && _target.TryGetTarget(out _);

            public void Handler(object? sender, EventArgs e)
            {
                if (!_item.TryGetTarget(out ToolStripItem? item) || !_target.TryGetTarget(out Target? target)) { Unsubscribe(_command, this); return; }
                // The owning strip knows its UI thread once it has a handle; before that, fall back to the binding
                // thread's context. With neither there is no UI thread to protect, so update in place.
                Control? owner = item.Owner;
                if (owner is not null && owner.IsHandleCreated && !owner.IsDisposed && owner.InvokeRequired)
                {
                    try { owner.BeginInvoke(new Action(Update)); }
                    catch (InvalidOperationException) { } // the handle went away meanwhile: nothing left to update
                }
                else if ((owner is null || !owner.IsHandleCreated) && _context is not null && Environment.CurrentManagedThreadId != _thread)
                    _context.Post(_ => Update(), null);
                else
                    item.Enabled = _command.CanExecute(target.Parameter);
            }

            private void Update()
            {
                if (_item.TryGetTarget(out ToolStripItem? item) && !item.IsDisposed && _target.TryGetTarget(out Target? target))
                    item.Enabled = _command.CanExecute(target.Parameter);
            }
        }
    }
}
