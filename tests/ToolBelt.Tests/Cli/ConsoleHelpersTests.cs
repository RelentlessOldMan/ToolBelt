using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Cli;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Cli
{
    /// <summary>AnsiStyle, ConsoleSpinner, Prompt and ConsoleApp.</summary>
    public sealed class ConsoleHelpersTests
    {
        // ---------- ANSI ----------

        private static Func<string, string?> Env(params (string Key, string Value)[] vars)
        {
            var d = vars.ToDictionary(v => v.Key, v => v.Value);
            return k => d.TryGetValue(k, out var v) ? v : null;
        }

        public void ColorDecision_FollowsTheConventions()
        {
            Check.True(AnsiStyle.ShouldUseColor(Env(), outputRedirected: false));
            Check.False(AnsiStyle.ShouldUseColor(Env(), outputRedirected: true));
            Check.False(AnsiStyle.ShouldUseColor(Env(("NO_COLOR", "1")), false));
            Check.False(AnsiStyle.ShouldUseColor(Env(("NO_COLOR", "1"), ("FORCE_COLOR", "1")), false));   // NO_COLOR wins
            Check.True(AnsiStyle.ShouldUseColor(Env(("NO_COLOR", "")), false));                          // empty = unset
            Check.True(AnsiStyle.ShouldUseColor(Env(("FORCE_COLOR", "1")), true));
            Check.False(AnsiStyle.ShouldUseColor(Env(("FORCE_COLOR", "0")), false));
            Check.True(AnsiStyle.ShouldUseColor(Env(("CLICOLOR_FORCE", "1")), true));
            Check.False(AnsiStyle.ShouldUseColor(Env(("TERM", "dumb")), false));
        }

        public void Styles_EmitSgrAndStripCleanly()
        {
            var on = new AnsiStyle(true);
            Check.Equal("\u001b[31mx\u001b[0m", on.Foreground("x", AnsiColor.Red));
            Check.Equal("\u001b[92mx\u001b[0m", on.Foreground("x", AnsiColor.BrightGreen));
            Check.Equal("\u001b[44mx\u001b[0m", on.Background("x", AnsiColor.Blue));
            Check.Equal("\u001b[38;5;208mx\u001b[0m", on.Foreground256("x", 208));
            Check.Equal("\u001b[38;2;1;2;3mx\u001b[0m", on.Rgb("x", 1, 2, 3));
            Check.Equal("\u001b[1;4;31mx\u001b[0m", on.Combine("x", AnsiColor.Red, bold: true, underline: true));
            string nested = on.Bold("a" + on.Error("b") + "c");
            Check.Equal("abc", AnsiText.Strip(nested));
            Check.True(nested.Contains("\u001b[0m\u001b[1mc"), "outer style is restored after an inner reset");
            Check.Equal(3, TextAlign.DisplayWidth(nested));
            Check.Throws<ArgumentOutOfRangeException>(() => on.Foreground256("x", 256));
        }

        public void Disabled_ReturnsPlainText()
        {
            var off = new AnsiStyle(false);
            Check.Equal("x", off.Foreground("x", AnsiColor.Red));
            Check.Equal("x", off.Combine("x", AnsiColor.Red, bold: true));
            Check.Equal("x", off.Rgb("x", 9, 9, 9));
        }

        // ---------- spinner ----------

        public void Spinner_InteractiveRedrawsAndErases()
        {
            var w = new StringWriter();
            using (var s = new ConsoleSpinner("work", w, interactive: true, frames: ConsoleSpinner.Ascii, interval: TimeSpan.FromHours(1)))
            {
                s.Tick();
                s.Message = "longer message";
                s.Message = "short";
                s.Stop("done");
            }
            string o = w.ToString();
            Check.True(o.StartsWith("\r| work\r/ work\r/ longer message\r/ short         ", StringComparison.Ordinal), o);
            Check.True(o.EndsWith("\r" + new string(' ', 7) + "\rdone" + Environment.NewLine, StringComparison.Ordinal), o);
        }

        public void Spinner_NonInteractiveWritesPlainLines()
        {
            var w = new StringWriter();
            var s = new ConsoleSpinner("step 1", w, interactive: false);
            s.Tick();
            s.Message = "step 2";
            s.Dispose();
            s.Dispose();                                                              // idempotent
            Check.Equal("step 1" + Environment.NewLine + "step 2" + Environment.NewLine, w.ToString());
        }

        public void Spinner_AnimatesInTheBackground()
        {
            var w = new StringWriter();
            using (new ConsoleSpinner("bg", TextWriter.Synchronized(w), interactive: true, frames: ConsoleSpinner.Ascii, interval: TimeSpan.FromMilliseconds(5)))
                Thread.Sleep(150);
            Check.True(w.ToString().Split('\r').Length > 5, "several frames were drawn");
        }

        // ---------- prompt ----------

        private static (Prompt Prompt, StringWriter Output) Make(string input, bool interactive = true)
        {
            var o = new StringWriter();
            return (new Prompt(new StringReader(input), o, interactive), o);
        }

        public void Confirm_AcceptsVariantsAndDefaults()
        {
            Check.True(Make("Y\n").Prompt.Confirm("Go?"));
            Check.False(Make("no\n").Prompt.Confirm("Go?"));
            Check.True(Make("\n").Prompt.Confirm("Go?", defaultValue: true));
            var (p, o) = Make("maybe\ny\n");
            Check.True(p.Confirm("Go?", false));
            Check.True(o.ToString().Contains("Go? [y/N]: Please answer y or n."), o.ToString());
        }

        public void TextIntegerAndChoice()
        {
            var (p, o) = Make("\nname\n");
            Check.Equal("name", p.Text("Name"));
            Check.True(o.ToString().Contains("A value is required."));
            Check.Equal("dflt", Make("\n").Prompt.Text("Name", "dflt"));
            Check.True(Make("\n").Output.ToString().Length == 0);
            var (ip, io) = Make("x\n99\n7\n");
            Check.Equal(7, ip.Integer("Count", 1, 10));
            Check.Equal(2, io.ToString().Split(new[] { "Please enter a whole number from 1 to 10." }, StringSplitOptions.None).Length - 1);
            Check.Equal(1, Make("2\n").Prompt.Choose("Pick", new[] { "red", "green" }));
            Check.Equal(0, Make("RED\n").Prompt.Choose("Pick", new[] { "red", "green" }));
            Check.Equal(1, Make("\n").Prompt.Choose("Pick", new[] { "red", "green" }, defaultIndex: 1));
        }

        public void NonInteractive_UsesDefaultsOrThrows()
        {
            var (p, o) = Make("ignored\n", interactive: false);
            Check.True(p.Confirm("Go?", true));
            Check.Equal(3, p.Integer("N", defaultValue: 3));
            Check.Equal(0, p.Choose("Pick", new[] { "a" }, 0));
            Check.Equal("", o.ToString());                                            // nothing was asked
            var ex = Check.Throws<NonInteractiveException>(() => p.Text("API key"));
            Check.True(ex.Message.Contains("API key"), ex.Message);
        }

        public void EndOfInput_IsNotAHang()
        {
            Check.Equal(5, Make("").Prompt.Integer("N", defaultValue: 5));
            Check.Throws<NonInteractiveException>(() => Make("").Prompt.Confirm("Go?"));
            var p = Make("a\nb\nc\n").Prompt;
            p.MaxAttempts = 3;
            Check.Throws<InvalidOperationException>(() => p.Integer("N"));
        }

        // ---------- ConsoleApp ----------

        private static async Task<(int Code, string Err)> RunApp(Func<CancellationToken, Task<int>> body, CancellationToken token = default)
        {
            var err = new StringWriter();
            int code = await ConsoleApp.RunAsync(body, err, verbose: false, hookConsole: false, externalToken: token);
            return (code, err.ToString());
        }

        public async Task ExitCodesFromOutcomes()
        {
            Check.Equal(0, (await RunApp(_ => Task.FromResult(0))).Code);
            Check.Equal(3, (await RunApp(_ => Task.FromResult(3))).Code);
            var exit = await RunApp(_ => throw new ExitException(ExitCodes.NoInput, "missing input.txt"));
            Check.Equal(66, exit.Code);
            Check.Equal("missing input.txt" + Environment.NewLine, exit.Err);
            var usage = await RunApp(_ => throw new ArgumentException("bad --level"));
            Check.Equal(ExitCodes.Usage, usage.Code);
            var crash = await RunApp(_ => throw new InvalidOperationException("boom"));
            Check.Equal(ExitCodes.Software, crash.Code);
            Check.Equal("error: boom (InvalidOperationException)" + Environment.NewLine, crash.Err);
        }

        public async Task CancellationMapsTo130()
        {
            using var cts = new CancellationTokenSource();
            var run = RunApp(async ct => { await Task.Delay(Timeout.Infinite, ct); return 0; }, cts.Token);
            cts.Cancel();
            var (code, err) = await run;
            Check.Equal(ExitCodes.Interrupted, code);
            Check.Equal("Cancelled." + Environment.NewLine, err);
            // A cancellation the app didn't ask for is a bug, not a Ctrl+C.
            Check.Equal(ExitCodes.Software, (await RunApp(_ => throw new OperationCanceledException())).Code);
        }

        public void SyncRunAndInterruptSource()
        {
            Check.Equal(4, ConsoleApp.Run(_ => 4, new StringWriter()));
            using var src = ConsoleApp.CreateInterruptSource();
            Check.False(src.IsCancellationRequested);
        }
    }
}
