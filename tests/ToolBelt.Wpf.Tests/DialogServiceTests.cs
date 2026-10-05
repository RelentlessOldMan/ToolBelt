using System;
using System.Windows;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class DialogServiceTests
    {
        // A view model written against IDialogService — the reason the abstraction exists.
        private sealed class DocumentViewModel
        {
            private readonly IDialogService _dialogs;
            public DocumentViewModel(IDialogService dialogs) => _dialogs = dialogs;
            public bool Dirty { get; set; } = true;
            public string? SavedTo { get; private set; }

            public bool TryClose()
            {
                if (!Dirty) return true;
                bool? save = _dialogs.AskYesNoCancel("Save changes?", "Closing");
                if (save is null) return false;
                if (save == true)
                {
                    string? path = _dialogs.SaveFile(new FileDialogOptions { Filter = "Text|*.txt", DefaultExtension = ".txt" });
                    if (path is null) return false;
                    SavedTo = path;
                }
                return true;
            }
        }

        public void Fake_DrivesAViewModelThroughScriptedAnswers()
        {
            var dialogs = new RecordingDialogService().EnqueueYesNoCancel(true).EnqueueSaveFile(@"C:\docs\a.txt");
            var vm = new DocumentViewModel(dialogs);
            Check.True(vm.TryClose());
            Check.Equal(@"C:\docs\a.txt", vm.SavedTo);
            Check.Equal(2, dialogs.Calls.Count);
            Check.Equal(DialogCallKind.YesNoCancel, dialogs.Calls[0].Kind);
            Check.Equal("Save changes?", dialogs.Calls[0].Message);
            Check.Equal("Closing", dialogs.Calls[0].Title);
            Check.Equal(".txt", dialogs.LastCall!.Options!.DefaultExtension);
            Check.Equal(0, dialogs.PendingResponses);
        }

        public void Fake_CancelPaths()
        {
            var cancelQuestion = new RecordingDialogService().EnqueueYesNoCancel(null);
            Check.False(new DocumentViewModel(cancelQuestion).TryClose());

            var cancelSave = new RecordingDialogService().EnqueueYesNoCancel(true).EnqueueSaveFile(null);
            Check.False(new DocumentViewModel(cancelSave).TryClose());
        }

        public void Fake_StrictThrowsOnUnexpectedDialog()
        {
            var dialogs = new RecordingDialogService();
            var ex = Check.Throws<InvalidOperationException>(() => dialogs.Confirm("Delete everything?"));
            Check.True(ex.Message.Contains("Confirm") && ex.Message.Contains("Delete everything?"), ex.Message);

            dialogs.Strict = false;
            Check.False(dialogs.Confirm("Delete everything?"));          // answered as "cancelled"
            Check.Null(dialogs.OpenFile());
            Check.Null(dialogs.AskYesNoCancel("?"));
        }

        public void Fake_QueuesArePerKindAndOrdered()
        {
            var dialogs = new RecordingDialogService()
                .EnqueueConfirm(true).EnqueueConfirm(false)
                .EnqueueOpenFiles("a.csv", "b.csv")
                .EnqueuePickFolder(@"C:\out");
            Check.Equal(4, dialogs.PendingResponses);
            Check.Equal(@"C:\out", dialogs.PickFolder("Output", @"C:\"));
            Check.True(dialogs.Confirm("first"));
            Check.False(dialogs.Confirm("second"));
            Check.Equal("a.csv,b.csv", string.Join(",", dialogs.OpenFiles()!));
            Check.Equal(@"C:\", dialogs.Calls[0].Options!.InitialDirectory);
            Check.Equal(0, dialogs.PendingResponses);
        }

        public void Fake_MessagesAreRecordedWithoutAnswers()
        {
            var dialogs = new RecordingDialogService();
            dialogs.ShowMessage("Saved.", "Info", DialogIcon.Information);
            dialogs.ShowMessage("Disk low.", icon: DialogIcon.Warning);
            Check.Equal(2, dialogs.Calls.Count);
            Check.Equal(DialogIcon.Warning, dialogs.LastCall!.Icon);
            dialogs.Reset();
            Check.Equal(0, dialogs.Calls.Count);
            Check.Null(dialogs.LastCall);
        }

        public void Real_IconMappingAndConstruction()
        {
            Check.Equal(MessageBoxImage.Warning, WpfDialogService.ToMessageBoxImage(DialogIcon.Warning));
            Check.Equal(MessageBoxImage.Error, WpfDialogService.ToMessageBoxImage(DialogIcon.Error));
            Check.Equal(MessageBoxImage.Question, WpfDialogService.ToMessageBoxImage(DialogIcon.Question));
            Check.Equal(MessageBoxImage.None, WpfDialogService.ToMessageBoxImage(DialogIcon.None));
            IDialogService real = new WpfDialogService(() => null); // constructing must not show anything
            Check.NotNull(real);
        }
    }
}
