using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LabSupport.Shared.Models;

namespace LabSupport.Manager.ViewModels;

public static class FileDistributionDialogHelper
{
    public static FileDistributionOptions? ShowDialog(
        Func<string?>? openFileDialogHandler = null,
        Func<string?>? folderBrowserDialogHandler = null)
    {
        if (Application.Current == null)
        {
            // Fallback for headless environments
            string? fallbackFile = openFileDialogHandler != null ? openFileDialogHandler() : null;
            if (string.IsNullOrWhiteSpace(fallbackFile)) return null;
            return new FileDistributionOptions
            {
                SourcePath = fallbackFile,
                IsDirectory = Directory.Exists(fallbackFile),
                ItemName = Path.GetFileName(fallbackFile),
                TargetDirectory = "Desktop",
                ShowNotification = true,
                Overwrite = true
            };
        }

        FileDistributionOptions? result = null;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new Window
            {
                Title = "Distribusi Berkas & Folder ke PC Siswa",
                Width = 520,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Foreground = Brushes.White
            };

            var root = new StackPanel { Margin = new Thickness(20) };

            // Title
            root.Children.Add(new TextBlock
            {
                Text = "📤 Pengiriman Berkas & Folder",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 16)
            });

            // Mode: File vs Folder
            var rbFile = new RadioButton
            {
                Content = "Kirim File Satuan",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 16, 0)
            };
            var rbFolder = new RadioButton
            {
                Content = "Kirim Satu Folder Utuh",
                Foreground = Brushes.White
            };
            var typePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };
            typePanel.Children.Add(rbFile);
            typePanel.Children.Add(rbFolder);
            root.Children.Add(typePanel);

            // Path selector row
            string selectedPath = string.Empty;
            var pathBox = new TextBox
            {
                IsReadOnly = true,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                Padding = new Thickness(8, 6, 8, 6),
                Width = 360,
                Text = "Belum ada berkas/folder yang dipilih..."
            };

            var btnBrowse = new Button
            {
                Content = "Pilih...",
                Width = 90,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                Foreground = Brushes.White
            };

            btnBrowse.Click += (_, _) =>
            {
                if (rbFolder.IsChecked == true)
                {
                    string? dir = folderBrowserDialogHandler != null ? folderBrowserDialogHandler() : null;
                    if (string.IsNullOrEmpty(dir))
                    {
                        var dlg = new Microsoft.Win32.OpenFolderDialog
                        {
                            Title = "Pilih Folder untuk Dikirim ke Siswa"
                        };
                        if (dlg.ShowDialog() == true) dir = dlg.FolderName;
                    }
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        selectedPath = dir;
                        pathBox.Text = dir;
                    }
                }
                else
                {
                    string? file = openFileDialogHandler != null ? openFileDialogHandler() : null;
                    if (string.IsNullOrEmpty(file))
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog
                        {
                            Title = "Pilih File untuk Dikirim ke Siswa"
                        };
                        if (dlg.ShowDialog() == true) file = dlg.FileName;
                    }
                    if (!string.IsNullOrWhiteSpace(file))
                    {
                        selectedPath = file;
                        pathBox.Text = file;
                    }
                }
            };

            var browseRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 16)
            };
            browseRow.Children.Add(pathBox);
            browseRow.Children.Add(btnBrowse);
            root.Children.Add(browseRow);

            // Target Directory Options
            root.Children.Add(new TextBlock
            {
                Text = "Lokasi Tujuan di PC Siswa:",
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var cmbTarget = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = Brushes.Black,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };
            cmbTarget.Items.Add("Desktop");
            cmbTarget.Items.Add("Documents");
            cmbTarget.Items.Add("Downloads");
            cmbTarget.Items.Add("Subfolder Kustom");
            cmbTarget.SelectedIndex = 0;
            root.Children.Add(cmbTarget);

            // Subfolder row
            var subfolderBox = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 16),
                Visibility = Visibility.Collapsed
            };
            cmbTarget.SelectionChanged += (_, _) =>
            {
                subfolderBox.Visibility = cmbTarget.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            };
            root.Children.Add(subfolderBox);

            // Delivery check options
            var chkAutoOpen = new CheckBox
            {
                Content = "Jalankan / Buka berkas otomatis setelah terkirim",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var chkNotification = new CheckBox
            {
                Content = "Tampilkan notifikasi pop-up di layar siswa",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var chkOverwrite = new CheckBox
            {
                Content = "Timpa file/folder jika sudah ada (Overwrite)",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 20)
            };
            root.Children.Add(chkAutoOpen);
            root.Children.Add(chkNotification);
            root.Children.Add(chkOverwrite);

            // Bottom action buttons
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var btnOk = new Button
            {
                Content = "Kirim Sekarang",
                Width = 120,
                Height = 34,
                IsDefault = true,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };
            btnOk.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(selectedPath) ||
                    (!File.Exists(selectedPath) && !Directory.Exists(selectedPath)))
                {
                    MessageBox.Show(window, "Silakan pilih berkas atau folder yang valid terlebih dahulu.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool isFolder = rbFolder.IsChecked == true || Directory.Exists(selectedPath);
                string targetDir = cmbTarget.SelectedItem?.ToString() ?? "Desktop";
                string sub = targetDir == "Subfolder Kustom" ? subfolderBox.Text.Trim() : string.Empty;

                result = new FileDistributionOptions
                {
                    SourcePath = selectedPath,
                    IsDirectory = isFolder,
                    ItemName = isFolder ? Path.GetFileName(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : Path.GetFileName(selectedPath),
                    TargetDirectory = targetDir == "Subfolder Kustom" ? "Desktop" : targetDir,
                    CustomSubfolder = sub,
                    AutoOpen = chkAutoOpen.IsChecked == true,
                    ShowNotification = chkNotification.IsChecked == true,
                    Overwrite = chkOverwrite.IsChecked == true
                };

                window.DialogResult = true;
                window.Close();
            };

            var btnCancel = new Button
            {
                Content = "Batal",
                Width = 90,
                Height = 34,
                IsCancel = true,
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                Foreground = Brushes.White
            };
            btnCancel.Click += (_, _) =>
            {
                window.DialogResult = false;
                window.Close();
            };

            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnCancel);
            root.Children.Add(btnRow);

            window.Content = root;
            window.ShowDialog();
        });

        return result;
    }
}
