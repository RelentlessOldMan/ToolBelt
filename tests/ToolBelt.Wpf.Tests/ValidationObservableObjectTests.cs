using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class ValidationObservableObjectTests
    {
        private sealed class Model : ValidationObservableObject
        {
            private string _email = "";
            public string Email
            {
                get => _email;
                set
                {
                    if (SetProperty(ref _email, value))
                        Validate();
                }
            }

            public void Validate()
            {
                ClearErrors(nameof(Email));
                if (string.IsNullOrWhiteSpace(Email))
                    AddError(nameof(Email), "Email is required.");
                else if (!Email.Contains('@'))
                    AddError(nameof(Email), "Email must contain '@'.");
            }

            public void Seed(params string[] errors) => SetErrors(nameof(Email), errors);
            public bool Reset() => ClearErrors(nameof(Email));
            public void WipeAll() => ClearAllErrors();
        }

        private static List<string> Errors(ValidationObservableObject o, string prop)
        {
            var list = new List<string>();
            foreach (var e in o.GetErrors(prop))
                list.Add((string)e);
            return list;
        }

        public void AddAndClear_TracksHasErrors()
        {
            var m = new Model();
            Check.False(m.HasErrors);

            m.Email = "nope"; // missing '@'
            Check.True(m.HasErrors);
            Check.Equal(1, Errors(m, "Email").Count);
            Check.Equal("Email must contain '@'.", Errors(m, "Email")[0]);

            m.Email = "a@b.com"; // valid
            Check.False(m.HasErrors);
            Check.Equal(0, Errors(m, "Email").Count);
        }

        public void ErrorsChanged_And_HasErrorsNotification_Fire()
        {
            var m = new Model();
            var errorProps = new List<string?>();
            var changedProps = new List<string?>();
            m.ErrorsChanged += (_, e) => errorProps.Add(e.PropertyName);
            m.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName);

            m.Seed("boom");
            Check.True(errorProps.Contains("Email"), "ErrorsChanged raised for Email");
            Check.True(changedProps.Contains("HasErrors"), "HasErrors change raised");
        }

        public void GetErrors_NullReturnsAll()
        {
            var m = new Model();
            m.Seed("e1", "e2");
            var all = new List<object>();
            foreach (var e in m.GetErrors(null))
                all.Add(e);
            Check.Equal(2, all.Count);
        }

        public void ClearAllErrors_Empties()
        {
            var m = new Model();
            m.Seed("x");
            Check.True(m.HasErrors);
            m.WipeAll();
            Check.False(m.HasErrors);
        }

        public void ImplementsINotifyDataErrorInfo()
        {
            INotifyDataErrorInfo info = new Model();
            Check.False(info.HasErrors);
        }
    }
}
