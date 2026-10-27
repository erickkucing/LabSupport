using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LabSupport.Manager.ViewModels;
using LabSupport.Shared.Protocol;
using RemoteMouseAction = LabSupport.Shared.Protocol.MouseAction;

namespace LabSupport.Manager.Views;

public partial class RemoteView : UserControl
{
    public RemoteView()
    {
        InitializeComponent();
    }

    private RemoteViewModel? ViewModel => DataContext as RemoteViewModel;

    private (double normX, double normY) GetNormalizedCoordinates(MouseEventArgs e, Image img)
    {
        var pos = e.GetPosition(img);
        double normX = img.ActualWidth > 0 ? Math.Clamp(pos.X / img.ActualWidth, 0.0, 1.0) : 0.0;
        double normY = img.ActualHeight > 0 ? Math.Clamp(pos.Y / img.ActualHeight, 0.0, 1.0) : 0.0;
        return (normX, normY);
    }

    private void OnScreenMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel == null || !ViewModel.IsConnected || !ViewModel.IsControlEnabled) return;
        if (sender is not Image img) return;

        var (normX, normY) = GetNormalizedCoordinates(e, img);

        if (e.ChangedButton == MouseButton.Left)
        {
            var action = e.ClickCount >= 2 ? RemoteMouseAction.DoubleClick : RemoteMouseAction.LeftClick;
            _ = ViewModel.SendMouseInputAsync(normX, normY, action);
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            _ = ViewModel.SendMouseInputAsync(normX, normY, RemoteMouseAction.RightClick);
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            _ = ViewModel.SendMouseInputAsync(normX, normY, RemoteMouseAction.MiddleClick);
        }
    }

    private void OnScreenMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel == null || !ViewModel.IsConnected || !ViewModel.IsControlEnabled) return;
        if (sender is not Image img) return;

        var (normX, normY) = GetNormalizedCoordinates(e, img);
        _ = ViewModel.SendMouseInputAsync(normX, normY, RemoteMouseAction.Scroll, e.Delta);
    }
}
