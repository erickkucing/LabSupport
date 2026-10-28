using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LabSupport.Shared.Protocol;

namespace LabSupport.Manager.ViewModels;

public static class NotificationDialogHelper
{
    public static readonly List<string> PresetTemplates = new()
    {
        "-- Pilih Template Pesan Cepat --",
        "⏳ Waktu praktikum tersisa 10 menit. Segera selesaikan tugas Anda.",
        "📖 Buka modul praktikum pada materi yang ditentukan guru.",
        "🤫 Harap tenang dan perhatikan penjelasan instruktur di depan kelas.",
        "💾 Simpan semua pekerjaan dan dokumen praktikum Anda sekarang.",
        "📥 Kumpulkan file hasil tugas ke folder pengumpulan server.",
        "🛑 Praktikum telah selesai. Rapikan meja dan jangan matikan PC sembarangan."
    };

    public static NotificationPayload? ShowDialog(int targetCount = 1)
    {
        if (Application.Current == null)
        {
            // Headless / unit test fallback
            return new NotificationPayload
            {
                Title = "Pengumuman Guru",
                Message = "Pesan pengumuman untuk praktikum.",
                Type = NotificationType.Info,
                DurationSeconds = 5,
                ShowHostInfo = false
            };
        }

        NotificationPayload? result = null;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new Window
            {
                Title = $"Kirim Notifikasi ke {targetCount} PC Siswa",
                Width = 540,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Foreground = Brushes.White
            };

            var root = new StackPanel { Margin = new Thickness(24) };

            // Header Title
            root.Children.Add(new TextBlock
            {
                Text = "📢 Kirim Notifikasi ke Layar PC Siswa",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 16)
            });

            // Template selector
            root.Children.Add(new TextBlock
            {
                Text = "Template Pesan Cepat (Preset):",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var comboTemplate = new ComboBox
            {
                ItemsSource = PresetTemplates,
                SelectedIndex = 0,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = Brushes.Black,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(comboTemplate);

            // Title input
            root.Children.Add(new TextBlock
            {
                Text = "Judul Notifikasi:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var txtTitle = new TextBox
            {
                Text = "Pengumuman Guru / Laboran",
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(txtTitle);

            // Message input
            root.Children.Add(new TextBlock
            {
                Text = "Isi Pesan Notifikasi:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var txtMessage = new TextBox
            {
                Text = "Harap tenang dan perhatikan instruksi guru di depan kelas.",
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 75,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(txtMessage);

            comboTemplate.SelectionChanged += (_, _) =>
            {
                int idx = comboTemplate.SelectedIndex;
                if (idx > 0 && idx < PresetTemplates.Count)
                {
                    txtMessage.Text = PresetTemplates[idx];
                }
            };

            // Notification Type and Duration in 2 columns
            var optGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            optGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            optGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Column 1: Type
            var typePanel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            typePanel.Children.Add(new TextBlock
            {
                Text = "Tipe Notifikasi:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var comboType = new ComboBox
            {
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = Brushes.Black,
                ItemsSource = new[]
                {
                    "ℹ️ Informasi (Biru)",
                    "⚠️ Peringatan (Kuning)",
                    "📢 Pengumuman Penting (Merah)"
                },
                SelectedIndex = 0
            };
            typePanel.Children.Add(comboType);
            Grid.SetColumn(typePanel, 0);
            optGrid.Children.Add(typePanel);

            // Column 2: Duration
            var durPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            durPanel.Children.Add(new TextBlock
            {
                Text = "Durasi Auto-Close:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var comboDuration = new ComboBox
            {
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = Brushes.Black,
                ItemsSource = new[]
                {
                    "3 Detik (Cepat)",
                    "5 Detik (Standar)",
                    "10 Detik (Lama)",
                    "Manual (Sampai Ditutup)"
                },
                SelectedIndex = 1
            };
            durPanel.Children.Add(comboDuration);
            Grid.SetColumn(durPanel, 1);
            optGrid.Children.Add(durPanel);

            root.Children.Add(optGrid);

            // Host Info Checkbox
            var chkHostInfo = new CheckBox
            {
                Content = "Sertakan Banner Identifikasi Komputer (Nama Komputer & Alamat IP)",
                IsChecked = false,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 18)
            };
            root.Children.Add(chkHostInfo);

            // Buttons: Kirim & Batal
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "Batal",
                Width = 90,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                Foreground = Brushes.White
            };
            btnCancel.Click += (_, _) => window.Close();
            btnPanel.Children.Add(btnCancel);

            var btnSend = new Button
            {
                Content = "Kirim Notifikasi",
                Width = 140,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            btnSend.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(txtMessage.Text))
                {
                    MessageBox.Show("Pesan notifikasi tidak boleh kosong.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var notifType = comboType.SelectedIndex switch
                {
                    1 => NotificationType.Warning,
                    2 => NotificationType.Urgent,
                    _ => NotificationType.Info
                };

                int duration = comboDuration.SelectedIndex switch
                {
                    0 => 3,
                    1 => 5,
                    2 => 10,
                    _ => 0
                };

                result = new NotificationPayload
                {
                    Title = txtTitle.Text.Trim(),
                    Message = txtMessage.Text.Trim(),
                    Type = notifType,
                    DurationSeconds = duration,
                    ShowHostInfo = chkHostInfo.IsChecked == true
                };

                window.Close();
            };
            btnPanel.Children.Add(btnSend);

            root.Children.Add(btnPanel);
            window.Content = root;
            window.ShowDialog();
        });

        return result;
    }
}
