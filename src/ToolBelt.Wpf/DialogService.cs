// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ToolBelt.Wpf
{
    /// <summary>The icon shown with a message or question.</summary>
    public enum DialogIcon
    {
        None,
        Information,
        Warning,
        Error,
        Question,
    }

    /// <summary>Options for the file dialogs.</summary>
    public sealed class FileDialogOptions
    {
        public string? Title { get; set; }

        /// <summary>A WPF/Win32 filter string, e.g. <c>"Images|*.png;*.jpg|All files|*.*"</c>.</summary>
        public string? Filter { get; set; }

        public string? InitialDirectory { get; set; }

        /// <summary>Pre-filled file name (Save) or initial selection (Open).</summary>
        public string? FileName { get; set; }

        /// <summary>Extension appended when the user types a name without one, e.g. <c>".csv"</c>.</summary>
        public string? DefaultExtension { get; set; }

        /// <summary>Ask before overwriting an existing file (Save). Default true.</summary>
        public bool OverwritePrompt { get; set; } = true;
    }

    /// <summary>
    /// Dialogs as a service, so view models can ask questions and pick files without touching UI types — and so
    /// tests can answer for the user. Use <see cref="WpfDialogService"/> in the app and
    /// <see cref="RecordingDialogService"/> in tests. Every method returns the "cancelled" value
    /// (<c>false</c> / <c>null</c>) when the user dismisses the dialog.
    /// </summary>
    public interface IDialogService
    {
        void ShowMessage(string message, string? title = null, DialogIcon icon = DialogIcon.Information);

        /// <summary>A Yes/No question; true for Yes.</summary>
        bool Confirm(string message, string? title = null, DialogIcon icon = DialogIcon.Question);

        /// <summary>A Yes/No/Cancel question; null for Cancel.</summary>
        bool? AskYesNoCancel(string message, string? title = null, DialogIcon icon = DialogIcon.Question);

        string? OpenFile(FileDialogOptions? options = null);

        IReadOnlyList<string>? OpenFiles(FileDialogOptions? options = null);

        string? SaveFile(FileDialogOptions? options = null);

        string? PickFolder(string? title = null, string? initialDirectory = null);
    }

    /// <summary>
    /// The real <see cref="IDialogService"/>: WPF <see cref="MessageBox"/> and the <c>Microsoft.Win32</c> file and
    /// folder dialogs, owned by the window the <c>owner</c> callback returns (by default the active window, else the
    /// main window) so dialogs stay on top of the app. Call from the UI thread.
    /// </summary>
    public sealed class WpfDialogService : IDialogService
    {
        private readonly Func<Window?> _owner;

        public WpfDialogService(Func<Window?>? owner = null) => _owner = owner ?? DefaultOwner;

        public void ShowMessage(string message, string? title = null, DialogIcon icon = DialogIcon.Information)
            => Show(message, title, MessageBoxButton.OK, icon);

        public bool Confirm(string message, string? title = null, DialogIcon icon = DialogIcon.Question)
            => Show(message, title, MessageBoxButton.YesNo, icon) == MessageBoxResult.Yes;

        public bool? AskYesNoCancel(string message, string? title = null, DialogIcon icon = DialogIcon.Question)
            => Show(message, title, MessageBoxButton.YesNoCancel, icon) switch
            {
                MessageBoxResult.Yes => true,
                MessageBoxResult.No => false,
                _ => null,
            };

        public string? OpenFile(FileDialogOptions? options = null)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = false };
            Apply(dlg, options);
            return dlg.ShowDialog(_owner()) == true ? dlg.FileName : null;
        }

        public IReadOnlyList<string>? OpenFiles(FileDialogOptions? options = null)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
            Apply(dlg, options);
            return dlg.ShowDialog(_owner()) == true ? dlg.FileNames : null;
        }

        public string? SaveFile(FileDialogOptions? options = null)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { OverwritePrompt = options?.OverwritePrompt ?? true };
            Apply(dlg, options);
            return dlg.ShowDialog(_owner()) == true ? dlg.FileName : null;
        }

        public string? PickFolder(string? title = null, string? initialDirectory = null)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog();
            if (title != null) dlg.Title = title;
            if (initialDirectory != null) dlg.InitialDirectory = initialDirectory;
            return dlg.ShowDialog(_owner()) == true ? dlg.FolderName : null;
        }

        /// <summary>The <see cref="MessageBoxImage"/> for a <see cref="DialogIcon"/>.</summary>
        public static MessageBoxImage ToMessageBoxImage(DialogIcon icon) => icon switch
        {
            DialogIcon.Information => MessageBoxImage.Information,
            DialogIcon.Warning => MessageBoxImage.Warning,
            DialogIcon.Error => MessageBoxImage.Error,
            DialogIcon.Question => MessageBoxImage.Question,
            _ => MessageBoxImage.None,
        };

        private MessageBoxResult Show(string message, string? title, MessageBoxButton buttons, DialogIcon icon)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));
            Window? owner = _owner();
            return owner != null
                ? MessageBox.Show(owner, message, title ?? string.Empty, buttons, ToMessageBoxImage(icon))
                : MessageBox.Show(message, title ?? string.Empty, buttons, ToMessageBoxImage(icon));
        }

        private static void Apply(Microsoft.Win32.FileDialog dlg, FileDialogOptions? o)
        {
            if (o is null) return;
            if (o.Title != null) dlg.Title = o.Title;
            if (o.Filter != null) dlg.Filter = o.Filter;
            if (o.InitialDirectory != null) dlg.InitialDirectory = o.InitialDirectory;
            if (o.FileName != null) dlg.FileName = o.FileName;
            if (o.DefaultExtension != null) { dlg.DefaultExt = o.DefaultExtension; dlg.AddExtension = true; }
        }

        private static Window? DefaultOwner()
        {
            Application? app = Application.Current;
            if (app is null) return null;
            return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
        }
    }

    /// <summary>The kind of dialog a <see cref="DialogCall"/> recorded.</summary>
    public enum DialogCallKind
    {
        Message,
        Confirm,
        YesNoCancel,
        OpenFile,
        OpenFiles,
        SaveFile,
        PickFolder,
    }

    /// <summary>One dialog request captured by <see cref="RecordingDialogService"/>, with the answer it was given.</summary>
    public sealed class DialogCall
    {
        internal DialogCall(DialogCallKind kind, string? message, string? title, DialogIcon icon, FileDialogOptions? options, object? response)
        {
            Kind = kind;
            Message = message;
            Title = title;
            Icon = icon;
            Options = options;
            Response = response;
        }

        public DialogCallKind Kind { get; }

        /// <summary>The message text (message boxes), or the title for <see cref="DialogCallKind.PickFolder"/>.</summary>
        public string? Message { get; }
        public string? Title { get; }
        public DialogIcon Icon { get; }
        public FileDialogOptions? Options { get; }

        /// <summary>The scripted answer returned to the caller.</summary>
        public object? Response { get; }

        public override string ToString() => Kind + (Message is null ? "" : ": " + Message);
    }

    /// <summary>
    /// A test double for <see cref="IDialogService"/>: it never shows UI, records every request in <see cref="Calls"/>,
    /// and answers from per-kind queues of scripted responses (<see cref="EnqueueConfirm"/> and friends). In
    /// <see cref="Strict"/> mode (the default) a dialog with no scripted answer throws, so a test cannot silently pass
    /// through a question it did not expect; otherwise it answers as if the user cancelled.
    /// </summary>
    public sealed class RecordingDialogService : IDialogService
    {
        private readonly List<DialogCall> _calls = new List<DialogCall>();
        private readonly Dictionary<DialogCallKind, Queue<object?>> _responses = new Dictionary<DialogCallKind, Queue<object?>>();

        /// <summary>Throw when a dialog has no scripted answer (default), instead of answering "cancelled".</summary>
        public bool Strict { get; set; } = true;

        /// <summary>Every dialog requested so far, in order.</summary>
        public IReadOnlyList<DialogCall> Calls => _calls;

        /// <summary>The most recent request, or null.</summary>
        public DialogCall? LastCall => _calls.Count == 0 ? null : _calls[_calls.Count - 1];

        /// <summary>Scripted answers not yet consumed — assert it is 0 to prove every expected dialog was shown.</summary>
        public int PendingResponses => _responses.Values.Sum(q => q.Count);

        public RecordingDialogService EnqueueConfirm(bool answer) => Enqueue(DialogCallKind.Confirm, answer);
        public RecordingDialogService EnqueueYesNoCancel(bool? answer) => Enqueue(DialogCallKind.YesNoCancel, answer);

        /// <summary>Scripts an Open answer; null simulates the user cancelling.</summary>
        public RecordingDialogService EnqueueOpenFile(string? path) => Enqueue(DialogCallKind.OpenFile, path);
        public RecordingDialogService EnqueueOpenFiles(params string[]? paths) => Enqueue(DialogCallKind.OpenFiles, paths);
        public RecordingDialogService EnqueueSaveFile(string? path) => Enqueue(DialogCallKind.SaveFile, path);
        public RecordingDialogService EnqueuePickFolder(string? path) => Enqueue(DialogCallKind.PickFolder, path);

        /// <summary>Forgets recorded calls and unconsumed answers.</summary>
        public void Reset()
        {
            _calls.Clear();
            _responses.Clear();
        }

        public void ShowMessage(string message, string? title = null, DialogIcon icon = DialogIcon.Information)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));
            _calls.Add(new DialogCall(DialogCallKind.Message, message, title, icon, null, null));
        }

        public bool Confirm(string message, string? title = null, DialogIcon icon = DialogIcon.Question)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));
            object? r = Answer(DialogCallKind.Confirm, message, false);
            _calls.Add(new DialogCall(DialogCallKind.Confirm, message, title, icon, null, r));
            return (bool)r!;
        }

        public bool? AskYesNoCancel(string message, string? title = null, DialogIcon icon = DialogIcon.Question)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));
            object? r = Answer(DialogCallKind.YesNoCancel, message, null);
            _calls.Add(new DialogCall(DialogCallKind.YesNoCancel, message, title, icon, null, r));
            return (bool?)r;
        }

        public string? OpenFile(FileDialogOptions? options = null) => File(DialogCallKind.OpenFile, options) as string;

        public IReadOnlyList<string>? OpenFiles(FileDialogOptions? options = null) => File(DialogCallKind.OpenFiles, options) as string[];

        public string? SaveFile(FileDialogOptions? options = null) => File(DialogCallKind.SaveFile, options) as string;

        public string? PickFolder(string? title = null, string? initialDirectory = null)
        {
            object? r = Answer(DialogCallKind.PickFolder, title, null);
            _calls.Add(new DialogCall(DialogCallKind.PickFolder, title, title, DialogIcon.None,
                new FileDialogOptions { Title = title, InitialDirectory = initialDirectory }, r));
            return r as string;
        }

        private object? File(DialogCallKind kind, FileDialogOptions? options)
        {
            object? r = Answer(kind, options?.Title, null);
            _calls.Add(new DialogCall(kind, null, options?.Title, DialogIcon.None, options, r));
            return r;
        }

        private RecordingDialogService Enqueue(DialogCallKind kind, object? answer)
        {
            if (!_responses.TryGetValue(kind, out var q)) _responses[kind] = q = new Queue<object?>();
            q.Enqueue(answer);
            return this;
        }

        private object? Answer(DialogCallKind kind, string? what, object? cancelled)
        {
            if (_responses.TryGetValue(kind, out var q) && q.Count > 0) return q.Dequeue();
            if (Strict)
                throw new InvalidOperationException(
                    $"No scripted answer for {kind}{(what is null ? "" : " (\"" + what + "\")")}. Enqueue one, or set Strict = false.");
            return cancelled;
        }
    }
}
