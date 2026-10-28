using System;
using System.Collections.Generic;
using System.Linq;
using LabSupport.Shared.Models;

namespace LabSupport.Shared.Services;

public static class ScriptPresetService
{
    public static IReadOnlyList<ScriptRecipe> GetDefaultPresets()
    {
        return new List<ScriptRecipe>
        {
            GetDeepCleanPreset(),
            GetPersiapanUjianPreset(),
            GetNormalisasiPascaUjianPreset(),
            GetLuncurkanAplikasiLabPreset(),
            GetPasangSoftwareStandarPreset(),
            GetMatikanLabTerjadwalPreset()
        };
    }

    public static ScriptRecipe? GetPresetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return GetDefaultPresets().FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public static ScriptRecipe GetDeepCleanPreset()
    {
        return new ScriptRecipe
        {
            Id = "preset-deep-clean",
            Name = "Deep Clean",
            Description = "Bersihkan cache temporary Windows, browser, dan reset cache DNS jaringan.",
            Author = "LabSupport Presets",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Steps = new List<ScriptActionStep>
            {
                new(ScriptActionType.ClearTempCache, "Bersihkan Cache & Temp")
                {
                    Description = "Menghapus temporary files pengguna, sistem, dan Recycle Bin."
                },
                new(ScriptActionType.FlushDnsNetwork, "Flush DNS & Jaringan")
                {
                    Description = "Mereset cache DNS dan memperbarui konfigurasi DHCP IP."
                },
                new(ScriptActionType.KillProcess, "Tutup Google Chrome")
                {
                    Description = "Menghentikan proses browser Chrome yang berjalan di latar belakang.",
                    ProcessName = "chrome"
                },
                new(ScriptActionType.KillProcess, "Tutup Microsoft Edge")
                {
                    Description = "Menghentikan proses browser Edge yang berjalan di latar belakang.",
                    ProcessName = "msedge"
                }
            }
        };
    }

    public static ScriptRecipe CreateDeepCleanPreset() => GetDeepCleanPreset();

    public static ScriptRecipe GetPersiapanUjianPreset()
    {
        return new ScriptRecipe
        {
            Id = "preset-exam-prep",
            Name = "Persiapan Ujian",
            Description = "Kunci penyimpanan USB, matikan volume master, tutup aplikasi terlarang, dan buka browser ke halaman ujian.",
            Author = "LabSupport Presets",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Steps = new List<ScriptActionStep>
            {
                new(ScriptActionType.SetUsbStorageLock, "Kunci Akses USB")
                {
                    Description = "Mencegah siswa menyambungkan flashdisk saat ujian.",
                    UsbLocked = true
                },
                new(ScriptActionType.SetSystemVolume, "Matikan Suara Master (Mute)")
                {
                    Description = "Menyetel volume master ke 0% agar ruang lab tenang.",
                    VolumePercent = 0
                },
                new(ScriptActionType.KillProcess, "Tutup Discord")
                {
                    Description = "Menghentikan aplikasi chatting yang tidak diizinkan saat ujian.",
                    ProcessName = "discord"
                },
                new(ScriptActionType.KillProcess, "Tutup Telegram")
                {
                    Description = "Menghentikan aplikasi chatting yang tidak diizinkan saat ujian.",
                    ProcessName = "telegram"
                },
                new(ScriptActionType.OpenUrlBrowser, "Buka Portal Ujian")
                {
                    Description = "Membuka browser siswa langsung ke URL ujian sekolah.",
                    TargetUrl = "https://ujian.sekolah.sch.id"
                }
            }
        };
    }

    public static ScriptRecipe CreateExamPrepPreset() => GetPersiapanUjianPreset();

    public static ScriptRecipe GetNormalisasiPascaUjianPreset()
    {
        return new ScriptRecipe
        {
            Id = "preset-post-exam",
            Name = "Normalisasi Pasca Ujian",
            Description = "Buka kunci penyimpanan USB, kembalikan volume normal (50%), bersihkan file sementara, dan flush DNS.",
            Author = "LabSupport Presets",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Steps = new List<ScriptActionStep>
            {
                new(ScriptActionType.SetUsbStorageLock, "Buka Kunci Akses USB")
                {
                    Description = "Mengembalikan akses penyimpanan USB ke kondisi normal.",
                    UsbLocked = false
                },
                new(ScriptActionType.SetSystemVolume, "Pulihkan Volume Master (50%)")
                {
                    Description = "Mengembalikan volume audio PC ke level default.",
                    VolumePercent = 50
                },
                new(ScriptActionType.ClearTempCache, "Bersihkan File Sementara")
                {
                    Description = "Membersihkan sisa download dan cache pasca ujian."
                },
                new(ScriptActionType.FlushDnsNetwork, "Flush DNS & Jaringan")
                {
                    Description = "Mereset tabel cache DNS klien."
                }
            }
        };
    }

    public static ScriptRecipe CreatePostExamPreset() => GetNormalisasiPascaUjianPreset();

    public static ScriptRecipe GetLuncurkanAplikasiLabPreset()
    {
        return new ScriptRecipe
        {
            Id = "preset-launch-lab",
            Name = "Luncurkan Aplikasi Lab",
            Description = "Meluncurkan executable interaktif di layar siswa dengan hak Administrator tanpa dialog konfirmasi UAC.",
            Author = "LabSupport Presets",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Steps = new List<ScriptActionStep>
            {
                new(ScriptActionType.LaunchExecutable, "Luncurkan Google Chrome (Interaktif Admin)")
                {
                    Description = "Membuka browser Google Chrome secara otomatis pada layar siswa dengan hak admin.",
                    ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    Arguments = "--start-maximized",
                    LaunchMode = ExecutableLaunchMode.InteractiveElevated
                }
            }
        };
    }

    public static ScriptRecipe CreateLaunchLabAppPreset() => GetLuncurkanAplikasiLabPreset();

    public static ScriptRecipe GetPasangSoftwareStandarPreset()
    {
        return new ScriptRecipe
        {
            Id = "preset-install-software",
            Name = "Pasang Software Standar",
            Description = "Instalasi software standar lab secara silent menggunakan Windows Package Manager (winget).",
            Author = "LabSupport Presets",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Steps = new List<ScriptActionStep>
            {
                new(ScriptActionType.InstallWingetSoftware, "Pasang Google Chrome")
                {
                    Description = "Mengunduh dan memasang Google Chrome tanpa dialog interaktif.",
                    SoftwarePackageId = "Google.Chrome"
                },
                new(ScriptActionType.InstallWingetSoftware, "Pasang Mozilla Firefox")
                {
                    Description = "Mengunduh dan memasang Mozilla Firefox tanpa dialog interaktif.",
                    SoftwarePackageId = "Mozilla.Firefox"
                }
            }
        };
    }

    public static ScriptRecipe CreateInstallStandardSoftwarePreset() => GetPasangSoftwareStandarPreset();

    public static ScriptRecipe GetMatikanLabTerjadwalPreset()
    {
        return new ScriptRecipe
        {
            Id = "preset-scheduled-shutdown",
            Name = "Matikan Lab Terjadwal",
            Description = "Pembersihan cache dan shutdown komputer lab dengan jeda pemberitahuan 60 detik.",
            Author = "LabSupport Presets",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Steps = new List<ScriptActionStep>
            {
                new(ScriptActionType.ClearTempCache, "Bersihkan Cache")
                {
                    Description = "Membersihkan cache sistem sebelum komputer dimatikan."
                },
                new(ScriptActionType.ShutdownRestart, "Matikan Komputer (Jeda 60 Detik)")
                {
                    Description = "Mengirimkan perintah shutdown dengan waktu tunggu 60 detik agar siswa sempat menyimpan data.",
                    IsRestart = false,
                    DelaySeconds = 60
                }
            }
        };
    }

    public static ScriptRecipe CreateScheduledShutdownPreset() => GetMatikanLabTerjadwalPreset();
}
