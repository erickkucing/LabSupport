using System;
using System.ComponentModel;
using System.Windows;
using LabSupport.Manager.ViewModels;

namespace LabSupport.Manager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
