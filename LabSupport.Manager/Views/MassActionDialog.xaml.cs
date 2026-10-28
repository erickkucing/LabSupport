using System;
using System.Windows;
using LabSupport.Manager.ViewModels;

namespace LabSupport.Manager.Views;

public partial class MassActionDialog : Window
{
    public MassActionDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MassActionViewModel vm)
        {
            vm.RequestClose = () =>
            {
                DialogResult = true;
                Close();
            };
        }
    }
}
