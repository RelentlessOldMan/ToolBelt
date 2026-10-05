using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class FileDropBehaviorTests
    {
        private static IDataObject Files(params string[] paths) => new DataObject(DataFormats.FileDrop, paths);

        public void GetFiles_FiltersByExtensionCaseInsensitively()
        {
            RunSta(() =>
            {
                IDataObject data = Files(@"C:\in\a.csv", @"C:\in\B.TXT", @"C:\in\notes.md", @"C:\in\folder");
                Check.Equal(@"C:\in\a.csv|C:\in\B.TXT", string.Join("|", FileDropBehavior.GetFiles(data, ".csv; txt")));
                Check.Equal(@"C:\in\notes.md", string.Join("|", FileDropBehavior.GetFiles(data, "*.md")));
                Check.Equal(4, FileDropBehavior.GetFiles(data, null).Length);    // no filter: everything, folders too
                Check.Equal(0, FileDropBehavior.GetFiles(new DataObject(DataFormats.UnicodeText, "hello"), null).Length);
            });
        }

        public void EffectsFor_RequiresFilesAndAnExecutableCommand()
        {
            RunSta(() =>
            {
                IDataObject data = Files(@"C:\in\a.csv");
                Check.Equal(DragDropEffects.Copy, FileDropBehavior.EffectsFor(data, new RelayCommand<object>(_ => { }), ".csv"));
                Check.Equal(DragDropEffects.None, FileDropBehavior.EffectsFor(data, null, ".csv"));
                Check.Equal(DragDropEffects.None, FileDropBehavior.EffectsFor(data, new RelayCommand<object>(_ => { }), ".png"));
                Check.Equal(DragDropEffects.None, FileDropBehavior.EffectsFor(data, new RelayCommand<object>(_ => { }, _ => false), ".csv"));

                // CanExecute receives the filtered paths, so a command can reject e.g. more than one file.
                object? seen = null;
                FileDropBehavior.EffectsFor(Files("x.csv", "y.png"), new RelayCommand<object>(_ => { }, p => { seen = p; return true; }), ".csv");
                Check.Equal("x.csv", string.Join("|", (string[])seen!));
            });
        }

        public void SettingTheCommandEnablesDrop()
        {
            RunSta(() =>
            {
                var target = new Border();
                Check.False(target.AllowDrop);
                FileDropBehavior.SetCommand(target, new RelayCommand<object>(_ => { }));
                FileDropBehavior.SetExtensions(target, ".csv");
                Check.True(target.AllowDrop);
                Check.Equal(".csv", FileDropBehavior.GetExtensions(target));
                FileDropBehavior.SetCommand(target, null); // detaches without throwing
                Check.Null(FileDropBehavior.GetCommand(target));
            });
        }

        internal static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error is not null) throw error;
        }
    }
}
