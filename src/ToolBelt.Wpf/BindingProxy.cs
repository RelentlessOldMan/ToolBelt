// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System.Windows;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// A <see cref="Freezable"/> that carries a single <see cref="Data"/> value so a binding can reach a
    /// DataContext that is otherwise out of its visual/logical tree — the standard trick for binding inside
    /// <c>DataGridColumn</c>, <c>ContextMenu</c>, or <c>Popup</c> content. Declare one as a resource with
    /// its <see cref="Data"/> bound to the view model, then bind through
    /// <c>{Binding Data.SomeProperty, Source={StaticResource proxy}}</c>. Being a <see cref="Freezable"/>,
    /// it participates in resource inheritance and carries the DataContext across the tree boundary.
    /// </summary>
    public sealed class BindingProxy : Freezable
    {
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));

        /// <summary>The carried value (typically the view model / DataContext).</summary>
        public object? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        protected override Freezable CreateInstanceCore() => new BindingProxy();
    }
}
