// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
        /// The dropped file paths that pass the <paramref name="extensions"/> filter (case-insensitive, on the end of the
        /// name, so ".tar.gz" works; "*.*" or "*" accepts every file). With a filter, folders are excluded — even one whose name ends in a listed extension;
        /// without one, everything in the drop is returned. Empty when the drop holds no files, or when its source fails to
        /// render them.
        /// </summary>
        public static string[] GetFiles(IDataObject data, string? extensions)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            return Filter(RawFiles(data), extensions);
        }

        // A foreign OLE drop source may advertise files and then fail to render them (DV_E_FORMATETC): the COMException
        // escaped the drag handlers.
        private static string[]? RawFiles(IDataObject data)
        {
            try { return data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] : null; }
            catch (ExternalException) { return null; }
        }

        private static bool HasFileDrop(IDataObject data)
        {
            try { return data.GetDataPresent(DataFormats.FileDrop); }
            catch (ExternalException) { return false; }
        }

        private static string[] Filter(string[]? paths, string? extensions)
        {
            if (paths is null) return Array.Empty<string>();
            var accepted = ParseExtensions(extensions, out bool anyFile);
            if (accepted.Count == 0 && !anyFile) return paths;
            var result = new List<string>();
            foreach (string p in paths)
                if (p != null && (anyFile || HasExtension(p, accepted)) && !Directory.Exists(p)) result.Add(p);
            return result.ToArray();
        }

        /// <summary>The drag effect to show: Copy when there are acceptable files and the command can execute them.</summary>
        public static DragDropEffects EffectsFor(IDataObject data, ICommand? command, string? extensions)
        {
            if (command is null) return DragDropEffects.None;
            string[] files = GetFiles(data, extensions);
            return files.Length > 0 && command.CanExecute(files) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        // Path.GetExtension gave only the last one, so ".tar.gz" never matched.
        private static bool HasExtension(string path, HashSet<string> accepted)
        {
            string name = Path.GetFileName(path);
            foreach (string e in accepted)
                if (name.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static HashSet<string> ParseExtensions(string? extensions, out bool anyFile)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            anyFile = false;
            if (string.IsNullOrWhiteSpace(extensions)) return set;
            foreach (string raw in extensions!.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string e = raw.Trim();
                if (e == "*" || e == "*.*" || e == ".*") { anyFile = true; continue; }
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
            element.PreviewDragLeave -= OnDragLeave;
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
                element.PreviewDragLeave += OnDragLeave;
                element.PreviewDrop += OnDrop;
            }
        }

        private static void OnDragOver(object sender, DragEventArgs e)
        {
            if (!(sender is DependencyObject d)) return;
            if (!HasFileDrop(e.Data)) return;      // text and other drags: leave them to the control
            ICommand? command = GetCommand(d);
            string? extensions = GetExtensions(d);
            // DragOver fires continuously while hovering, and filtering touches the disk once per file (Directory.Exists):
            // a drag of thousands of files froze the UI for the whole drag. Filter once per drag — keyed on the data object
            // and its paths, since a source may refill the same object with other files between drags.
            string[] raw = RawFiles(e.Data) ?? Array.Empty<string>();
            DragCache? c = _drag;
            if (c is null || !c.Data.TryGetTarget(out IDataObject? cached) || !ReferenceEquals(cached, e.Data)
                || c.Extensions != extensions || !SamePaths(c.Raw, raw))
                _drag = c = new DragCache(e.Data, raw, extensions, Filter(raw, extensions));
            e.Effects = command != null && c.Files.Length > 0 && command.CanExecute(c.Files) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        [ThreadStatic] private static DragCache? _drag;

        private sealed class DragCache
        {
            public DragCache(IDataObject data, string[] raw, string? extensions, string[] files)
            {
                Data = new WeakReference<IDataObject>(data);
                Raw = raw;
                Extensions = extensions;
                Files = files;
            }

            public WeakReference<IDataObject> Data { get; }
            public string[] Raw { get; }
            public string? Extensions { get; }
            public string[] Files { get; }
        }

        private static bool SamePaths(string[] a, string[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        // The cache held the last drag's paths for the life of the thread.
        private static void OnDragLeave(object sender, DragEventArgs e) => _drag = null;

        private static void OnDrop(object sender, DragEventArgs e)
        {
            _drag = null;
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
