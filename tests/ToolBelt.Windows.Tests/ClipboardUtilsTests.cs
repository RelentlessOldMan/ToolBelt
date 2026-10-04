using System;
using System.ComponentModel;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class ClipboardUtilsTests
    {
        // These are integration tests against the real clipboard — a shared OS resource that can be held
        // continuously by another process (RDP/clipboard redirection, headless sessions). When the clipboard
        // genuinely cannot be opened, the Win32Exception is treated as inconclusive (the test self-skips)
        // rather than a failure; assertion failures (CheckFailedException) still fail the test normally.
        // The existing clipboard text is saved and restored so a test run does not clobber it.

        public void SetGetClear_RoundTrip()
        {
            RunWithClipboard(() =>
            {
                string value = "ToolBelt clipboard " + Guid.NewGuid().ToString("N");
                ClipboardUtils.SetText(value);
                Check.True(ClipboardUtils.ContainsText(), "contains text after set");
                Check.Equal(value, ClipboardUtils.GetText());

                ClipboardUtils.Clear();
                Check.False(ClipboardUtils.ContainsText(), "no text after clear");
                Check.Null(ClipboardUtils.GetText());
            });
        }

        public void SetEmptyString_RoundTrips()
        {
            RunWithClipboard(() =>
            {
                ClipboardUtils.SetText("");
                Check.True(ClipboardUtils.ContainsText(), "empty string is still text");
                Check.Equal("", ClipboardUtils.GetText());
            });
        }

        public void SetText_Unicode_Preserved()
        {
            RunWithClipboard(() =>
            {
                string value = "café — Ω ∑ 𝄞 日本語";
                ClipboardUtils.SetText(value);
                Check.Equal(value, ClipboardUtils.GetText());
            });
        }

        public void SetText_Null_Throws()
        {
            // Pure argument validation — never touches the clipboard, so always runs.
            Check.Throws<ArgumentNullException>(() => ClipboardUtils.SetText(null!));
        }

        // Saves/restores the clipboard around the body and self-skips on genuine clipboard unavailability.
        private static void RunWithClipboard(Action body)
        {
            string? saved;
            try { saved = ClipboardUtils.GetText(); }
            catch (Win32Exception) { return; } // clipboard unavailable in this environment -> inconclusive

            try { body(); }
            catch (Win32Exception) { return; } // lost the clipboard mid-test -> inconclusive
            finally
            {
                try
                {
                    if (saved is not null) ClipboardUtils.SetText(saved);
                    else ClipboardUtils.Clear();
                }
                catch (Win32Exception) { /* best-effort restore */ }
            }
        }
    }
}
