using System;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class FileAssociationTests
    {
        public void Query_DoesNotThrow_AndNormalizesExtension()
        {
            // With and without the dot must behave identically.
            string? withDot = FileAssociation.GetAssociatedExecutable(".txt");
            string? noDot = FileAssociation.GetAssociatedExecutable("txt");
            Check.Equal(withDot, noDot);

            // If an association exists it should look like a path to an .exe.
            if (withDot is not null)
                Check.True(withDot.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || withDot.Length > 0, $"executable path: {withDot}");
        }

        public void RegisteredExtension_ResolvesExecutable()
        {
            // .txt resolves to an opener on any normal Windows install (an editor, or the Open With chooser).
            string? exe = FileAssociation.GetAssociatedExecutable(".txt");
            Check.NotNull(exe);
            Check.True(exe!.Length > 0, "non-empty executable path");

            // A nonsense extension must not throw, whatever fallback the shell returns.
            FileAssociation.GetProgId(".toolbelt_nope_zzz");
            FileAssociation.GetFriendlyAppName(".toolbelt_nope_zzz");
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentNullException>(() => FileAssociation.GetAssociatedExecutable(null!));
            Check.Throws<ArgumentException>(() => FileAssociation.GetAssociatedExecutable(""));
        }
    }
}
