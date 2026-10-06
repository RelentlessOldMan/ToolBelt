// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// Attached behavior that accepts files dropped onto any element and hands their paths (a <c>string[]</c>) to a
    /// bound <see cref="ICommand"/>:
    /// <c>&lt;ListBox tb:FileDropBehavior.Command="{Binding ImportCommand}" tb:FileDropBehavior.Extensions=".csv;.txt"/&gt;</c>.
    /// While dragging, the cursor shows Copy only when the drop holds at least one acceptable file and the command
    /// can execute them, otherwise None — so users see before releasing whether a drop will be taken. Setting the
    /// command also sets <c>AllowDrop</c>; clearing it detaches everything and restores the previous <c>AllowDrop</c>. The
    /// tunnelling (Preview) drag events are used, so controls with their own drag handling (a <c>TextBox</c> accepts dragged
    /// text) do not swallow files — and only drags carrying files are handled, so that text drag-and-drop keeps working.
    /// No dependency on an external behaviors package.
    /// </summary>
    public static class FileDropBehavior
    {
        public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
            "Command", typeof(ICommand), typeof(FileDropBehavior), new PropertyMetadata(null, OnCommandChanged));

        /// <summary>Accepted extensions separated by ';' or ',' (e.g. ".csv;.txt"); empty accepts any file or folder.</summary>
        public static readonly DependencyProperty ExtensionsProperty = DependencyProperty.RegisterAttached(
            "Extensions", typeof(string), typeof(FileDropBehavior), new PropertyMetadata(null));

        public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);
        public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);
        public static string? GetExtensions(DependencyObject element) => (string?)element.GetValue(ExtensionsProperty);
        public static void SetExtensions(DependencyObject element, string? value) => element.SetValue(ExtensionsProperty, value);

        /// <summary>
        /// The dropped file paths that pass the <paramref name="extensions"/> filter (case-insensitive). With a filter,
        /// folders are excluded; without one, everything in the drop is returned. Empty when the drop holds no files.
        /// </summary>
        public static string[] GetFiles(IDataObject data, string? extensions)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (!data.GetDataPresent(DataFormats.FileDrop) || !(data.GetData(DataFormats.FileDrop) is string[] paths))
                return Array.Empty<string>();
            var accepted = ParseExtensions(extensions);
            if (accepted.Count == 0) return paths;
            var result = new List<string>();
            foreach (string p in paths)
                if (p != null && accepted.Contains(Path.GetExtension(p))) result.Add(p);
            return result.ToArray();
        }

        /// <summary>The drag effect to show: Copy when there are acceptable files and the command can execute them.</summary>
        public static DragDropEffects EffectsFor(IDataObject data, ICommand? command, string? extensions)
        {
            if (command is null) return DragDropEffects.None;
            string[] files = GetFiles(data, extensions);
            return files.Length > 0 && command.CanExecute(files) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private static HashSet<string> ParseExtensions(string? extensions)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(extensions)) return set;
            foreach (string raw in extensions!.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string e = raw.Trim();
                if (e.StartsWith("*", StringComparison.Ordinal)) e = e.Substring(1);
                if (e.Length == 0) continue;
                set.Add(e.StartsWith(".", StringComparison.Ordinal) ? e : "." + e);
            }
            return set;
        }

        // AllowDrop as it was before the behavior turned it on, restored when the command is cleared.
        private static readonly DependencyProperty PriorAllowDropProperty =
            DependencyProperty.RegisterAttached("PriorAllowDrop", typeof(bool?), typeof(FileDropBehavior), new PropertyMetadata(null));

        private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is UIElement element)) return;
            element.PreviewDragEnter -= OnDragOver;
            element.PreviewDragOver -= OnDragOver;
            element.PreviewDrop -= OnDrop;
            if (!(e.NewValue is ICommand) && element.GetValue(PriorAllowDropProperty) is bool prior)
            {
                element.AllowDrop = prior;
                element.ClearValue(PriorAllowDropProperty);
            }
            if (e.NewValue is ICommand)
            {
                if (element.GetValue(PriorAllowDropProperty) is null) element.SetValue(PriorAllowDropProperty, element.AllowDrop);
                element.AllowDrop = true;
                element.PreviewDragEnter += OnDragOver;
                element.PreviewDragOver += OnDragOver;
                element.PreviewDrop += OnDrop;
            }
        }

        private static void OnDragOver(object sender, DragEventArgs e)
        {
            if (!(sender is DependencyObject d)) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;      // text and other drags: leave them to the control
            e.Effects = EffectsFor(e.Data, GetCommand(d), GetExtensions(d));
            e.Handled = true;
        }

        private static void OnDrop(object sender, DragEventArgs e)
        {
            if (!(sender is DependencyObject d)) return;
            ICommand? command = GetCommand(d);
            string[] files = GetFiles(e.Data, GetExtensions(d));
            if (command != null && files.Length > 0 && command.CanExecute(files))
            {
                command.Execute(files);
                e.Handled = true;
            }
        }
    }
}
