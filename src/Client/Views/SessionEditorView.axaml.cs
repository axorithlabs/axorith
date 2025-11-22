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

        AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (e.Source is not Control control)
            {
                return;
            }

            var comboBox = control.FindAncestorOfType<ComboBox>();

            if (comboBox == null || comboBox.IsDropDownOpen)
            {
                return;
            }

            e.Handled = true;

            var scrollViewer = comboBox.FindAncestorOfType<ScrollViewer>();
            if (scrollViewer == null)
            {
                return;
            }

            switch (e.Delta.Y)
            {
                case > 0:
                    scrollViewer.LineUp();
                    break;
                case < 0:
                    scrollViewer.LineDown();
                    break;
            }

            switch (e.Delta.X)
            {
                case > 0:
                    scrollViewer.LineLeft();
                    break;
                case < 0:
                    scrollViewer.LineRight();
                    break;
            }
        }, RoutingStrategies.Tunnel);

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Source is not TextBox textBox || (e.Key != Key.Enter && e.Key != Key.Escape))
            {
                return;
            }

            if (e.Key == Key.Enter && textBox.AcceptsReturn)
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            #pragma warning disable CS0618
            topLevel?.FocusManager?.ClearFocus();
            #pragma warning restore CS0618

            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }
}