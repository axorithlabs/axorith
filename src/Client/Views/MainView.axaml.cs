using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Axorith.Client.ViewModels;

namespace Axorith.Client.Views;

public partial class MainView : UserControl
{
    private Window? _window;
    private MainViewModel? _observedViewModel;
    private bool _dialogWasOpen;
    private int? _emergencyUnlockPointerId;
    private bool _emergencyUnlockKeyHeld;

    public MainView()
    {
        InitializeComponent();
        AddHandler(InputElement.PointerPressedEvent, OnRootPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.PointerReleasedEvent, OnRootPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.KeyDownEvent, OnRootKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.KeyUpEvent, OnRootKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += (_, _) => ObserveViewModel();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ObserveViewModel();
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window != null)
        {
            _window.Deactivated += OnWindowDeactivated;
        }

        UpdateDialogFocus();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_observedViewModel != null)
        {
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _observedViewModel = null;
        }

        if (_window != null)
        {
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }

        CancelEmergencyUnlockInput();
        _dialogWasOpen = false;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void ObserveViewModel()
    {
        if (ReferenceEquals(_observedViewModel, ViewModel))
        {
            return;
        }

        if (_observedViewModel != null)
        {
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _observedViewModel = ViewModel;
        if (_observedViewModel != null)
        {
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateDialogFocus();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsOverlayOpen))
        {
            UpdateDialogFocus();
        }
    }

    private void UpdateDialogFocus()
    {
        var isOpen = ViewModel?.IsOverlayOpen == true;
        if (!isOpen && !_dialogWasOpen)
        {
            return;
        }

        _dialogWasOpen = isOpen;
        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this) == null)
            {
                return;
            }

            Control? target;
            if (ViewModel?.IsEmergencyUnlockOpen == true)
                target = this.FindControl<Button>("EmergencyUnlockHoldButton");
            else if (ViewModel?.IsStartConfirmationOpen == true)
                target = this.FindControl<Button>("StartConfirmationCancelButton");
            else if (ViewModel is { IsSessionActive: true, IsHomePage: true })
                target = this.FindControl<Button>(ViewModel.IsCommittedSession
                    ? "OpenEmergencyUnlockButton"
                    : "StopSessionButton");
            else if (ViewModel?.IsSessionActive == true)
                target = this.FindControl<Button>(ViewModel.IsCommittedSession
                    ? "SidebarEmergencyUnlockButton"
                    : "SidebarStopSessionButton");
            else if (ViewModel is { IsPresetsPage: true, Presets.Count: > 0 })
                target = this.FindControl<ListBox>("PresetsListBox");
            else if (ViewModel?.IsPresetsPage == true)
                target = this.FindControl<Button>("CreateSessionButton");
            else
                target = this.FindControl<TextBlock>("TodaySectionHeading");

            if (target?.IsVisible != true)
            {
                target = ViewModel?.IsPresetsPage == true
                    ? this.FindControl<Button>("CreateSessionButton")
                    : this.FindControl<TextBlock>("TodaySectionHeading");
            }

            target?.Focus();
        });
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => CancelEmergencyUnlockInput();

    private void CancelEmergencyUnlockInput()
    {
        _emergencyUnlockPointerId = null;
        _emergencyUnlockKeyHeld = false;
        ViewModel?.EndEmergencyUnlockHold();
    }

    private Button? GetEmergencyUnlockButton() => this.FindControl<Button>("EmergencyUnlockHoldButton");

    private static bool IsWithin(Control control, Control ancestor)
    {
        for (var current = control; current != null; current = current.GetVisualParent() as Control)
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }

        return false;
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var button = GetEmergencyUnlockButton();
        if (ViewModel?.IsEmergencyUnlockOpen != true || button == null ||
            e.Source is not Control source ||
            !IsWithin(source, button) ||
            !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Pointer.Capture(button);
        _emergencyUnlockPointerId = e.Pointer.Id;
        ViewModel?.BeginEmergencyUnlockHold();
        e.Handled = true;
    }

    private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_emergencyUnlockPointerId != e.Pointer.Id)
        {
            return;
        }

        _emergencyUnlockPointerId = null;
        e.Pointer.Capture(null);
        ViewModel?.EndEmergencyUnlockHold();
        e.Handled = true;
    }

    private void OnEmergencyUnlockPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _emergencyUnlockPointerId = null;
        ViewModel?.EndEmergencyUnlockHold();
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel?.IsEmergencyUnlockOpen == true && GetEmergencyUnlockButton()?.IsFocused == true &&
            e.Key is (Key.Space or Key.Enter) && !_emergencyUnlockKeyHeld)
        {
            _emergencyUnlockKeyHeld = true;
            ViewModel?.BeginEmergencyUnlockHold();
            e.Handled = true;
        }
    }

    private void OnRootKeyUp(object? sender, KeyEventArgs e)
    {
        if (_emergencyUnlockKeyHeld && e.Key is (Key.Space or Key.Enter))
        {
            _emergencyUnlockKeyHeld = false;
            ViewModel?.EndEmergencyUnlockHold();
            e.Handled = true;
        }
    }

    private void OnEmergencyUnlockLostFocus(object? sender, RoutedEventArgs e)
    {
        _emergencyUnlockKeyHeld = false;
        ViewModel?.EndEmergencyUnlockHold();
    }
}
