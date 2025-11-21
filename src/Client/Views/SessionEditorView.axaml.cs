using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Axorith.Client.Views;

public partial class SessionEditorView : UserControl
{
    public SessionEditorView()
    {
        InitializeComponent();

        AddHandler(PointerWheelChangedEvent, (sender, e) =>
        {
            if (e.Source is not Control control || control.FindAncestorOfType<ComboBox>() is not { } comboBox)
            {
                return;
            }

            if (!comboBox.IsDropDownOpen)
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        AddHandler(KeyDownEvent, (sender, e) =>
        {
            if (e.Source is not TextBox textBox || (e.Key != Key.Enter && e.Key != Key.Escape))
            {
                return;
            }

            if (e.Key == Key.Enter && textBox.AcceptsReturn) 
                return;

            var topLevel = TopLevel.GetTopLevel(this);
            #pragma warning disable CS0618 
            topLevel?.FocusManager?.ClearFocus();
            #pragma warning restore CS0618 
                
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }
}