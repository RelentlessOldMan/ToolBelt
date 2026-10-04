using System.Collections.Generic;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class ObservableObjectTests
    {
        private sealed class Person : ObservableObject
        {
            private string _name = "";
            private int _age;
            public string Name { get => _name; set => SetProperty(ref _name, value); }
            public int Age { get => _age; set => SetProperty(ref _age, value); }
        }

        public void SetProperty_RaisesOnChange_AndReportsChanged()
        {
            var p = new Person();
            var raised = new List<string?>();
            p.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            p.Name = "Ada";
            Check.Equal(1, raised.Count);
            Check.Equal("Name", raised[0]);
            Check.Equal("Ada", p.Name);
        }

        public void SetProperty_NoNotificationWhenEqual()
        {
            var p = new Person { Age = 5 };
            int count = 0;
            p.PropertyChanged += (_, _) => count++;

            p.Age = 5; // unchanged
            Check.Equal(0, count);

            p.Age = 6; // changed
            Check.Equal(1, count);
        }
    }
}
