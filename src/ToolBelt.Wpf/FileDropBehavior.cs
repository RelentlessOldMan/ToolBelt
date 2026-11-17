// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// Attached behavior that accepts files dropped onto any element (a <see cref="UIElement"/>, <see cref="ContentElement"/>
    /// or <see cref="UIElement3D"/>) and hands their paths (a <c>string[]</c>) to a
    /// bound <see cref="ICommand"/>:
    /// <c>&lt;ListBox tb:FileDropBehavior.Command="{Binding ImportCommand}" tb:FileDropBehavior.Extensions=".csv;.txt"/&gt;</c>.
    /// While dragging, the cursor shows Copy only when the drop holds at least one acceptable file and the command
    /// can execute them, otherwise None — so users see before releasing whether a drop will be taken (Copy only when the
    /// source allows it; a drop the command takes reports Copy back to the source, never Move). Setting the
    /// command also sets <c>AllowDrop</c>; clearing it detaches everything and restores the previous <c>AllowDrop</c>. The
    /// tunnelling (Preview) drag events are used, so controls with their own drag handling (a <c>TextBox</c> accepts dragged
    /// text) do not swallow files — and only drags carrying files are handled, so that text drag-and-drop keeps working.
    /// With nested drop targets the innermost one under the pointer decides (its filter, its cursor, its command); an
    /// outer target steps aside. No dependency on an external behaviors package.
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
        /// name, so ".tar.gz" works; "*.*" or "*" accepts every file, "*." or "." a file with no extension). With a filter, folders are excluded — even one whose name ends in a listed extension;
        /// without one, every path in the drop is returned, folders included (a null entry never is). The array is always
        /// a new one. Empty when the drop holds no files, or when its source fails to render them.
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
            var result = new List<string>();
            // With no filter the source's own array went to the command, null entries and all.
            if (accepted.Count == 0 && !anyFile)
            {
                foreach (string p in paths)
                    if (p != null) result.Add(p);
                return result.ToArray();
            }
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
            {
                // "*." is the Windows filter for no extension; it matched only names ending in a dot, which Windows trims.
                if (e == ".") { if (Path.GetExtension(name).Length == 0) return true; }
                else if (name.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
            }
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

        // AllowDrop's local value as it was before the behavior turned it on (NoLocalValue when it had none: AllowDrop
        // inherits), restored when the command is cleared. Writing back the effective value made it local, so the element
        // stopped inheriting AllowDrop from its ancestors.
        private static readonly DependencyProperty PriorAllowDropProperty =
            DependencyProperty.RegisterAttached("PriorAllowDrop", typeof(object), typeof(FileDropBehavior), new PropertyMetadata(null));

        private static readonly object NoLocalValue = new object();

        // The prior value is boxed: stored directly, a binding or resource reference was attached to PriorAllowDrop
        // itself, which threw ("NonShareable ... already been used") or lost the DynamicResource on restore.
        private sealed class Prior
        {
            public readonly object Value;
            public Prior(object value) => Value = value;
        }

        private static readonly PropertyInfo? ResourceKeyOf = typeof(FrameworkElement).Assembly
            .GetType("System.Windows.ResourceReferenceExpression")?.GetProperty("ResourceKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static void Restore(DependencyObject d, object prior)
        {
            if (ReferenceEquals(prior, NoLocalValue)) { d.ClearValue(UIElement.AllowDropProperty); return; }
            if (prior is BindingExpressionBase binding) { BindingOperations.SetBinding(d, UIElement.AllowDropProperty, binding.ParentBindingBase); return; }
            if (prior is Expression)
            {
                object? key = ResourceKeyOf != null && ResourceKeyOf.DeclaringType!.IsInstanceOfType(prior) ? ResourceKeyOf.GetValue(prior) : null;
                if (key != null && d is FrameworkElement fe) { fe.SetResourceReference(UIElement.AllowDropProperty, key); return; }
                if (key != null && d is FrameworkContentElement fce) { fce.SetResourceReference(UIElement.AllowDropProperty, key); return; }
                d.ClearValue(UIElement.AllowDropProperty);   // an expression that can't be re-created: back to the inherited value
                return;
            }
            d.SetValue(UIElement.AllowDropProperty, prior);
        }

        // Elements the behavior has attached to: only these are nested targets. A Command on an element it could not
        // attach to made the outer target step aside with nothing taking the drop.
        private static readonly DependencyProperty AttachedProperty =
            DependencyProperty.RegisterAttached("Attached", typeof(bool), typeof(FileDropBehavior), new PropertyMetadata(false));

        private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // ContentElement and UIElement3D take drops too; only UIElement got the handlers.
            if (!(d is UIElement || d is ContentElement || d is UIElement3D) || !(d is IInputElement element)) return;
            element.RemoveHandler(DragDrop.PreviewDragEnterEvent, (DragEventHandler)OnDragOver);
            element.RemoveHandler(DragDrop.PreviewDragOverEvent, (DragEventHandler)OnDragOver);
            element.RemoveHandler(DragDrop.PreviewDragLeaveEvent, (DragEventHandler)OnDragLeave);
            element.RemoveHandler(DragDrop.PreviewDropEvent, (DragEventHandler)OnDrop);
            var prior = (Prior?)d.GetValue(PriorAllowDropProperty);
            if (!(e.NewValue is ICommand) && prior != null)
            {
                Restore(d, prior.Value);
                d.ClearValue(PriorAllowDropProperty);
                d.ClearValue(AttachedProperty);
            }
            if (e.NewValue is ICommand)
            {
                if (prior is null)
                {
                    object local = d.ReadLocalValue(UIElement.AllowDropProperty);
                    d.SetValue(PriorAllowDropProperty, new Prior(ReferenceEquals(local, DependencyProperty.UnsetValue) ? NoLocalValue : local));
                }
                d.SetValue(UIElement.AllowDropProperty, true);
                d.SetValue(AttachedProperty, true);
                element.AddHandler(DragDrop.PreviewDragEnterEvent, (DragEventHandler)OnDragOver);
                element.AddHandler(DragDrop.PreviewDragOverEvent, (DragEventHandler)OnDragOver);
                element.AddHandler(DragDrop.PreviewDragLeaveEvent, (DragEventHandler)OnDragLeave);
                element.AddHandler(DragDrop.PreviewDropEvent, (DragEventHandler)OnDrop);
            }
        }

        // The target sets a subset of what the source allows: Copy was shown (and a drop returned every allowed effect)
        // even when the source allowed only Move, and a source told Move may delete its originals.
        private static DragDropEffects CopyIfAllowed(DragEventArgs e)
            => (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;

        private static void OnDragOver(object sender, DragEventArgs e)
        {
            if (!(sender is DependencyObject d)) return;
            if (!HasFileDrop(e.Data)) return;      // text and other drags: leave them to the control
            if (InnerTargetBelow(d, e)) return;
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
            e.Effects = command != null && c.Files.Length > 0 && command.CanExecute(c.Files) ? CopyIfAllowed(e) : DragDropEffects.None;
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

        // The Preview events tunnel from the outside in, and the outer target marked them handled: a nested target never
        // saw the drop (the outer one ran its command, or refused files the inner one accepts).
        private static bool InnerTargetBelow(DependencyObject target, DragEventArgs e)
        {
            for (DependencyObject? d = e.OriginalSource as DependencyObject; d != null && !ReferenceEquals(d, target); d = Parent(d))
                if ((bool)d.GetValue(AttachedProperty) && GetCommand(d) != null) return true;
            return false;
        }

        private static DependencyObject? Parent(DependencyObject d)
        {
            if (d is Visual || d is Visual3D) return VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            return d is ContentElement ce ? ContentOperations.GetParent(ce) ?? LogicalTreeHelper.GetParent(d) : null;
        }

        // The cache held the last drag's paths for the life of the thread.
        private static void OnDragLeave(object sender, DragEventArgs e) => _drag = null;

        private static void OnDrop(object sender, DragEventArgs e)
        {
            if (!(sender is DependencyObject d)) return;
            if (InnerTargetBelow(d, e)) return;
            _drag = null;
            ICommand? command = GetCommand(d);
            string[] files = GetFiles(e.Data, GetExtensions(d));
            DragDropEffects effect = CopyIfAllowed(e);
            if (command != null && files.Length > 0 && effect != DragDropEffects.None && command.CanExecute(files))
            {
                e.Effects = effect;   // left as every allowed effect, Move included
                e.Handled = true;
                command.Execute(files);
            }
            else if (HasFileDrop(e.Data)) e.Effects = DragDropEffects.None;
        }
    }
}
