// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System.Windows.Input;

namespace ToolBelt.WinForms
{
    /// <summary>Parses and formats menu shortcuts such as <c>"Ctrl+Shift+S"</c> or <c>"F5"</c>.</summary>
    public static class Shortcut
    {
        /// <summary>Parses modifiers (Ctrl/Control, Alt, Shift) and one key ("S", "F5", "Delete", "1", "Plus").</summary>
        public static Keys Parse(string text)
        {
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
            Keys key;
            if (k.Length == 1 && char.IsDigit(k[0])) key = Keys.D0 + (k[0] - '0');
            else if (k.Equals("Plus", StringComparison.OrdinalIgnoreCase)) key = Keys.Oemplus;
            else if (k.Equals("Minus", StringComparison.OrdinalIgnoreCase)) key = Keys.OemMinus;
            else if (k.Equals("Del", StringComparison.OrdinalIgnoreCase)) key = Keys.Delete;
            else if (k.Equals("Esc", StringComparison.OrdinalIgnoreCase)) key = Keys.Escape;
            else if (!Enum.TryParse(k, ignoreCase: true, out key) || int.TryParse(k, out _)) { keys = Keys.None; return false; }
            if (key == Keys.None || (key & Keys.Modifiers) != 0 || key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu)
            {
                keys = Keys.None;
                return false;
            }
            keys |= key;
            return true;
        }

        /// <summary>"Ctrl+Shift+S" style text for display.</summary>
        public static string Format(Keys keys)
        {
            var parts = new List<string>();
            if ((keys & Keys.Control) != 0) parts.Add("Ctrl");
            if ((keys & Keys.Alt) != 0) parts.Add("Alt");
            if ((keys & Keys.Shift) != 0) parts.Add("Shift");
            Keys key = keys & Keys.KeyCode;
            parts.Add(key >= Keys.D0 && key <= Keys.D9 ? ((int)(key - Keys.D0)).ToString(CultureInfo.InvariantCulture)
                : key == Keys.Oemplus ? "Plus" : key == Keys.OemMinus ? "Minus" : key.ToString());
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

        /// <summary>An item bound to <paramref name="command"/>: clicking executes it, and Enabled tracks CanExecute.</summary>
        public MenuItemsBuilder Item(string text, ICommand command, object? parameter = null, string? shortcut = null, Image? image = null, string? name = null)
        {
            var item = Create(text, shortcut, image, name);
            CommandBinding.Bind(item, command, parameter);
            _items.Add(item);
            return this;
        }

        /// <summary>A check item; <paramref name="onChanged"/> receives the new state after each click.</summary>
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
                item.ShortcutKeys = Shortcut.Parse(shortcut);
                item.ShortcutKeyDisplayString = Shortcut.Format(item.ShortcutKeys);
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
        /// Finds an item by its path of texts, ignoring mnemonics and case, e.g. <c>"File/Recent/report.csv"</c>; null if absent.
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
                    if (string.Equals(PlainText(candidate.Text ?? ""), segment.Trim(), StringComparison.OrdinalIgnoreCase)) { found = candidate; break; }
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
    /// buttons, labels, separators and drop-down buttons. Text doubles as the tooltip unless one is given.
    /// </summary>
    public sealed class ToolStripBuilder
    {
        private readonly List<ToolStripItem> _items = new List<ToolStripItem>();

        public ToolStripBuilder Button(string text, Action onClick, Image? image = null, string? toolTip = null, string? name = null)
        {
            if (onClick is null) throw new ArgumentNullException(nameof(onClick));
            var b = CreateButton(text, image, toolTip, name);
            b.Click += (_, __) => onClick();
            _items.Add(b);
            return this;
        }

        public ToolStripBuilder Button(string text, ICommand command, object? parameter = null, Image? image = null, string? toolTip = null, string? name = null)
        {
            var b = CreateButton(text, image, toolTip, name);
            CommandBinding.Bind(b, command, parameter);
            _items.Add(b);
            return this;
        }

        public ToolStripBuilder Toggle(string text, bool isChecked, Action<bool> onChanged, Image? image = null, string? toolTip = null, string? name = null)
        {
            if (onChanged is null) throw new ArgumentNullException(nameof(onChanged));
            var b = CreateButton(text, image, toolTip, name);
            b.CheckOnClick = true;
            b.Checked = isChecked;
            b.CheckedChanged += (_, __) => onChanged(b.Checked);
            _items.Add(b);
            return this;
        }

        public ToolStripBuilder Label(string text, string? name = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            _items.Add(new ToolStripLabel(text) { Name = name ?? MenuBuilder.PlainText(text) });
            return this;
        }

        public ToolStripBuilder Separator()
        {
            _items.Add(new ToolStripSeparator());
            return this;
        }

        public ToolStripBuilder DropDown(string text, Action<MenuItemsBuilder> items, Image? image = null, string? name = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (items is null) throw new ArgumentNullException(nameof(items));
            var dd = new ToolStripDropDownButton(text, image) { Name = name ?? MenuBuilder.PlainText(text) };
            items(new MenuItemsBuilder(dd.DropDownItems));
            _items.Add(dd);
            return this;
        }

        public ToolStrip Build()
        {
            var strip = new ToolStrip();
            ApplyTo(strip);
            return strip;
        }

        public void ApplyTo(ToolStrip strip)
        {
            if (strip is null) throw new ArgumentNullException(nameof(strip));
            strip.Items.AddRange(_items.ToArray());
            _items.Clear(); // items now belong to that strip
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

    /// <summary>Wires a <see cref="ToolStripItem"/> to an <see cref="ICommand"/> and unhooks when the item is disposed.</summary>
    internal static class CommandBinding
    {
        public static void Bind(ToolStripItem item, ICommand command, object? parameter)
        {
            if (command is null) throw new ArgumentNullException(nameof(command));
            void Sync(object? s, EventArgs e) => item.Enabled = command.CanExecute(parameter);
            item.Click += (_, __) =>
            {
                if (command.CanExecute(parameter)) command.Execute(parameter);
            };
            command.CanExecuteChanged += Sync;
            item.Disposed += (_, __) => command.CanExecuteChanged -= Sync; // the command must not keep the item alive
            Sync(null, EventArgs.Empty);
        }
    }
}
