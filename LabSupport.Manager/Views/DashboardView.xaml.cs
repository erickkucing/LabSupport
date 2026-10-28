using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LabSupport.Manager.Models;
using LabSupport.Manager.ViewModels;

namespace LabSupport.Manager.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void OnTileDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is LabComputer comp)
        {
            double newX = Math.Max(10, comp.PositionX + e.HorizontalChange);
            double newY = Math.Max(10, comp.PositionY + e.VerticalChange);
            comp.PositionX = newX;
            comp.PositionY = newY;
        }
    }

    private void OnTileDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is LabComputer comp && DataContext is DashboardViewModel vm)
        {
            _ = vm.SaveComputerPositionAsync(comp.Hostname, comp.PositionX, comp.PositionY);
        }
    }
}
