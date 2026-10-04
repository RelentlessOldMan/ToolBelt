using System;
using System.Windows.Input;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class RelayCommandTests
    {
        public void Execute_AndCanExecute_Delegate()
        {
            int runs = 0;
            bool allowed = false;
            var cmd = new RelayCommand(() => runs++, () => allowed);

            Check.False(cmd.CanExecute(null));
            allowed = true;
            Check.True(cmd.CanExecute(null));

            cmd.Execute(null);
            Check.Equal(1, runs);
        }

        public void CanExecute_DefaultsTrue_WhenNoPredicate()
        {
            var cmd = new RelayCommand(() => { });
            Check.True(cmd.CanExecute(null));
        }

        public void RaiseCanExecuteChanged_FiresEvent()
        {
            var cmd = new RelayCommand(() => { });
            int fired = 0;
            cmd.CanExecuteChanged += (_, _) => fired++;
            cmd.RaiseCanExecuteChanged();
            Check.Equal(1, fired);
        }

        public void Generic_PassesTypedParameter()
        {
            int received = -1;
            var cmd = new RelayCommand<int>(p => received = p, p => p > 0);

            Check.True(cmd.CanExecute(5));
            Check.False(cmd.CanExecute(-1));
            cmd.Execute(42);
            Check.Equal(42, received);

            // A non-T parameter (null) maps to default(T) == 0.
            Check.False(cmd.CanExecute(null));
        }

        public void NullExecute_Throws()
        {
            Check.Throws<ArgumentNullException>(() => new RelayCommand(null!));
            Check.Throws<ArgumentNullException>(() => new RelayCommand<int>(null!));
        }

        public void ImplementsICommand()
        {
            ICommand cmd = new RelayCommand(() => { });
            Check.NotNull(cmd);
        }
    }
}
