using System;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    public sealed class ComboBoxEnumExtensionsTests
    {
        private enum Fruit { Apple, Banana, Cherry }

        public void BindEnum_PopulatesItems_AndSelectionRoundTrips()
        {
            RunSta(() =>
            {
                using var combo = new ComboBox();
                combo.BindEnum<Fruit>();
                Check.Equal(3, combo.Items.Count);

                Check.Null(combo.GetSelectedEnum<Fruit>()); // nothing selected yet

                combo.SetSelectedEnum(Fruit.Banana);
                Fruit? selected = combo.GetSelectedEnum<Fruit>();
                Check.True(selected == Fruit.Banana, $"selected Banana, got {selected}");

                // Re-binding clears the previous items.
                combo.BindEnum<Fruit>();
                Check.Equal(3, combo.Items.Count);
            });
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentNullException>(() => ComboBoxEnumExtensions.BindEnum<Fruit>(null!));
            Check.Throws<ArgumentNullException>(() => ComboBoxEnumExtensions.GetSelectedEnum<Fruit>(null!));
            Check.Throws<ArgumentNullException>(() => ComboBoxEnumExtensions.SetSelectedEnum(null!, Fruit.Apple));
        }

        private static void RunSta(Action body)
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
