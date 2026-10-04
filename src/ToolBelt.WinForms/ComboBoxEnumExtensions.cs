// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// Populates a <see cref="ComboBox"/> with the values of an enum and reads/writes the selection as the
    /// enum type. Adds the values directly to <see cref="ComboBox.Items"/> (no data-binding context needed),
    /// so it works before the control's handle is created.
    /// </summary>
    public static class ComboBoxEnumExtensions
    {
        /// <summary>Clears the combo and adds every value of <typeparamref name="TEnum"/> as an item.</summary>
        public static void BindEnum<TEnum>(this ComboBox comboBox) where TEnum : struct, Enum
        {
            if (comboBox is null) throw new ArgumentNullException(nameof(comboBox));
            comboBox.Items.Clear();
            foreach (TEnum value in Enum.GetValues<TEnum>())
                comboBox.Items.Add(value);
        }

        /// <summary>The currently-selected enum value, or null if nothing (or a non-enum item) is selected.</summary>
        public static TEnum? GetSelectedEnum<TEnum>(this ComboBox comboBox) where TEnum : struct, Enum
        {
            if (comboBox is null) throw new ArgumentNullException(nameof(comboBox));
            return comboBox.SelectedItem is TEnum value ? value : null;
        }

        /// <summary>Selects the item equal to <paramref name="value"/>.</summary>
        public static void SetSelectedEnum<TEnum>(this ComboBox comboBox, TEnum value) where TEnum : struct, Enum
        {
            if (comboBox is null) throw new ArgumentNullException(nameof(comboBox));
            comboBox.SelectedItem = value;
        }
    }
}
