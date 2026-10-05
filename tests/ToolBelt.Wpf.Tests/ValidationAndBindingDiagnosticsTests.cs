using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class ValidationAndBindingDiagnosticsTests
    {
        private sealed class Settings : RuleValidatedObject
        {
            private string? _name;
            private int _port = 80;
            private DateTime _start = new DateTime(2026, 1, 1), _end = new DateTime(2026, 1, 2);
            private string _email = "";

            public Settings()
            {
                Rules.For(nameof(Name), () => Name).Required().MinLength(3).MaxLength(10);
                Rules.For(nameof(Port), () => Port).Range(1, 65535);
                Rules.For(nameof(End), () => End).Must(e => e > Start, "End must be after Start.").DependsOn(nameof(Start));
                Rules.For(nameof(Email), () => Email).Matches(@"[^@\s]+@[^@\s]+\.[a-z]+", "Not an e-mail address.");
            }

            public string? Name { get => _name; set => SetAndValidate(ref _name, value); }
            public int Port { get => _port; set => SetAndValidate(ref _port, value); }
            public DateTime Start { get => _start; set => SetAndValidate(ref _start, value); }
            public DateTime End { get => _end; set => SetAndValidate(ref _end, value); }
            public string Email { get => _email; set => SetAndValidate(ref _email, value); }

            public string[] ErrorsOf(string p) => GetErrors(p).Cast<string>().ToArray();
        }

        public void Rules_RunOnSetAndStopAfterRequired()
        {
            var s = new Settings();
            var changed = new List<string?>();
            s.ErrorsChanged += (_, e) => changed.Add(e.PropertyName);
            s.Name = "  ";
            Check.Equal("Name is required.", string.Join("|", s.ErrorsOf(nameof(Settings.Name))));   // MinLength not also reported
            s.Name = "ab";
            Check.Equal("Name must be at least 3 characters.", string.Join("|", s.ErrorsOf(nameof(Settings.Name))));
            s.Name = "abcdef";
            Check.Equal(0, s.ErrorsOf(nameof(Settings.Name)).Length);
            Check.False(s.HasErrors);
            Check.True(changed.All(n => n == nameof(Settings.Name)) && changed.Count == 3);
            s.Port = 70000;
            Check.Equal("Port must be between 1 and 65535.", s.ErrorsOf(nameof(Settings.Port)).Single());
            s.Email = "nope";
            Check.Equal("Not an e-mail address.", s.ErrorsOf(nameof(Settings.Email)).Single());
            s.Email = "";                                                              // empty passes Matches (use Required for presence)
            Check.Equal(0, s.ErrorsOf(nameof(Settings.Email)).Length);
        }

        public void CrossFieldRuleRevalidatesTheDependent()
        {
            var s = new Settings { Name = "valid" };
            s.Start = new DateTime(2026, 2, 1);                                         // now after End
            Check.Equal("End must be after Start.", s.ErrorsOf(nameof(Settings.End)).Single());
            s.Start = new DateTime(2025, 12, 1);
            Check.Equal(0, s.ErrorsOf(nameof(Settings.End)).Length);
        }

        public void ValidateAll_ShowsUntouchedFields()
        {
            var s = new Settings();                                                    // Name never set
            Check.False(s.HasErrors);
            Check.False(s.ValidateAll());
            Check.Equal("Name is required.", s.ErrorsOf(nameof(Settings.Name)).Single());
            var standalone = new ValidationRuleSet();
            int value = 5;
            standalone.For("V", () => value).Range(1, 3).Must(v => v % 2 == 0, "even");
            Check.Equal("V must be between 1 and 3.|even", string.Join("|", standalone.Validate("V")));
            Check.Equal(0, standalone.Validate("unknown").Count);
            Check.Throws<ArgumentException>(() => standalone.For("W", () => 1).Range(5, 1));
        }

        public void XamlRules()
        {
            var inv = CultureInfo.InvariantCulture;
            Check.False(new RequiredValidationRule().Validate(" ", inv).IsValid);
            Check.True(new RequiredValidationRule().Validate("x", inv).IsValid);
            var range = new NumericRangeValidationRule { Minimum = 0, Maximum = 10 };
            Check.True(range.Validate("7.5", inv).IsValid);
            Check.False(range.Validate("11", inv).IsValid);
            Check.False(range.Validate("abc", inv).IsValid);
            Check.False(range.Validate("", inv).IsValid);
            Check.True(new NumericRangeValidationRule { AllowEmpty = true }.Validate("", inv).IsValid);
            Check.True(range.Validate("7,5", new CultureInfo("de-DE")).IsValid, "binding culture decides the decimal separator");
            var regex = new RegexValidationRule { Pattern = "[A-Z]{3}-\\d+" };
            Check.True(regex.Validate("ABC-12", inv).IsValid);
            Check.False(regex.Validate("xABC-12", inv).IsValid, "whole-value match");
            Check.True(regex.Validate("", inv).IsValid);
        }

        // ---------- binding diagnostics ----------

        private sealed class Model { public string Real { get; set; } = "hello"; }

        public void BindingFailures_AreCaptured()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                using var listener = new BindingFailureListener();
                var seen = new List<BindingFailure>();
                listener.BindingFailed += (_, f) => seen.Add(f);
                var tb = new TextBlock { DataContext = new Model() };
                tb.SetBinding(TextBlock.TextProperty, new Binding("Real"));
                Check.Equal("hello", tb.Text);
                Check.Equal(0, listener.Failures.Count);
                tb.SetBinding(TextBlock.TagProperty, new Binding("Missing"));
                Check.Equal(1, listener.Failures.Count);
                var f = listener.Failures[0];
                Check.Equal(40, f.Code);
                Check.Equal("Missing", f.Path);
                Check.Equal("Tag", f.TargetProperty);
                Check.Equal(1, seen.Count);
            });
        }

        public void BindingFailures_CanThrowAndDetach()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var listener = new BindingFailureListener { ThrowOnFailure = true };
                var tb = new TextBlock { DataContext = new Model() };
                bool threw = false;
                try { tb.SetBinding(TextBlock.TagProperty, new Binding("Nope")); }
                catch (BindingFailureException ex) { threw = ex.Failure.Path == "Nope"; }
                Check.True(threw, "a broken binding throws when ThrowOnFailure is set");
                listener.Dispose();
                tb.SetBinding(TextBlock.ToolTipProperty, new Binding("AlsoMissing"));    // detached: no throw, not recorded
                Check.Equal(1, listener.Failures.Count);
            });
        }

        public void DebugConverter_PassesThroughAndLogs()
        {
            var lines = new List<string>();
            var c = new DebugConverter { Log = lines.Add, Name = "Total" };
            Check.Equal(42, c.Convert(42, typeof(string), "p", CultureInfo.InvariantCulture));
            Check.Null(c.ConvertBack(null, typeof(int), null, CultureInfo.InvariantCulture));
            Check.Equal("[Total] Convert: value='42' (Int32) target=String parameter='p'", lines[0]);
            Check.Equal("[Total] ConvertBack: value=null target=Int32", lines[1]);
            FileDropBehaviorTests.RunSta(() =>
            {
                var tb = new TextBlock { DataContext = new Model() };
                tb.SetBinding(TextBlock.TextProperty, new Binding("Real") { Converter = c });
                Check.Equal("hello", tb.Text);
            });
            Check.True(lines.Count == 3 && lines[2].Contains("'hello' (String)"), string.Join(" / ", lines));
        }
    }
}
