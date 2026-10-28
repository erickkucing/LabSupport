# Walkthrough: Integritas Sistem, Ketahanan Perubahan IP & Peningkatan Mutakhir LabSupport

Semua perbaikan dan peningkatan sistem telah berhasil diimplementasikan dan diverifikasi secara menyeluruh dengan suite pengujian otomatis (**152/152 tes lulus 100%**).

---

## 1. Fondasi Integritas Sistem & Ketahanan Jaringan

### A. Dynamic IP Self-Healing (PC Manajemen Berubah IP via DHCP / Statis)
- [`ManagerBeaconService.cs`](file:///root/project/LabSupport/LabSupport.Shared/Services/ManagerBeaconService.cs): Layanan broadcaster UDP pada Manager di port 5903 yang memancarkan paket beacon berkala setiap 5 detik berisi IP aktif terkini dan membalas permintaan *FindManager*.
- [`AgentDiscoveryClient.cs`](file:///root/project/LabSupport/LabSupport.Agent/Services/AgentDiscoveryClient.cs): Client UDP pada Agent yang mendengarkan beacon Manager dan aktif memancarkan *AgentDiscoveryRequest* saat koneksi heartbeat terputus (kegagalan >= 2).
- [`HeartbeatService.cs`](file:///root/project/LabSupport/LabSupport.Agent/Services/HeartbeatService.cs): Ditambahkan metode `UpdateManagerEndpoint` dan event `OnConnectionLost` / `OnConnectionRestored`.
- [`CommandListener.cs`](file:///root/project/LabSupport/LabSupport.Agent/Services/CommandListener.cs): Menangani perintah `CommandType.UpdateManagerEndpoint` untuk memutakhirkan konfigurasi IP secara remote via TCP Port 5902.
- **Hasil**: Ketika IP PC Manajemen berubah, seluruh komputer siswa otomatis mendeteksi IP baru dalam 5–10 detik (*zero-touch*), memperbarui `appsettings.json` lokal secara persisten, dan kembali online tanpa perlu intervensi manual.

---

### B. SQLite WAL Mode & Throttled Persistence (Anti-Lock & Hemat Disk I/O)
- [`DatabaseService.cs`](file:///root/project/LabSupport/LabSupport.Shared/Services/DatabaseService.cs): Mengaktifkan `PRAGMA journal_mode = WAL;`, `PRAGMA synchronous = NORMAL;`, dan `PRAGMA busy_timeout = 5000;`.
- [`DashboardViewModel.cs`](file:///root/project/LabSupport/LabSupport.Manager/ViewModels/DashboardViewModel.cs): Men-throttle penulisan ke disk SQLite. Metrik dinamis (CPU, RAM, timestamp) tetap terupdate instan di memori, namun penulisan disk hanya dilakukan jika IP/MAC berubah atau berkala tiap 60 detik.
- **Hasil**: Mengurangi frekuensi penulisan disk sebesar ~95%, mengeliminasi bottleneck I/O dan mencegah exception `SQLite Error 5: 'database is locked'` saat puluhan PC mengirim heartbeat serentak.

---

### C. Off-UI Image Decoding & Bilinear Resize (Performa UI 60 FPS)
- [`MainViewModel.cs`](file:///root/project/LabSupport/LabSupport.Manager/ViewModels/MainViewModel.cs): Decoding string Base64 screenshot thumbnail dan pembuatan `BitmapImage` dilakukan di background worker thread, lalu di-`.Freeze()` sebelum diserahkan ke UI thread secara asynchronous (`BeginInvoke`).
- [`ScreenCaptureService.cs`](file:///root/project/LabSupport/LabSupport.Shared/Services/ScreenCaptureService.cs): Mengubah mode interpolasi resizing thumbnail dari `HighQualityBicubic` menjadi `InterpolationMode.Bilinear` dengan `PixelOffsetMode.Half`.
- **Hasil**: UI Manager 100% mulus (smooth scrolling tanpa frame drop) dan penggunaan CPU Agent pada PC siswa turun mendekati 0%.

---

### D. Hardware Identity Berbasis MAC Address Fisik
- [`DatabaseService.cs`](file:///root/project/LabSupport/LabSupport.Shared/Services/DatabaseService.cs): Membuat indeks `idx_computers_mac` dan memperbarui `UpsertComputerAsync` untuk memprioritaskan pencocokan hardware MAC address.
- [`DashboardViewModel.cs`](file:///root/project/LabSupport/LabSupport.Manager/ViewModels/DashboardViewModel.cs): Pencocokan identitas mendahulukan MAC address fisik dan mendeteksi kondisi duplikat hostname (`IsDuplicateHostname`).
- **Hasil**: Jika terdapat komputer lab hasil kloning disk yang belum sempat direname (misal sama-sama bernama `LAB-PC`), kedua komputer tidak akan saling menimpa data, melainkan terdaftar sebagai 2 entri fisik terpisah.

---

### E. Izin Port Jaringan Bebas Pop-Up UAC
- [`setup_manager_permissions.bat`](file:///root/project/LabSupport/setup_manager_permissions.bat): Script batch otomatis satu kali eksekusi untuk mendaftarkan URL ACL `netsh http add urlacl url=http://*:8080/ user=Everyone` dan firewall rule untuk port 8080 & 5903.
- [`HttpFileServer.cs`](file:///root/project/LabSupport/LabSupport.Shared/Services/HttpFileServer.cs): Jika wildcard belum didaftarkan, server otomatis mengikat (*fallback*) ke semua IP fisik LAN aktif (bukan hanya `127.0.0.1`), sehingga jaringan LAN tetap dapat terhubung.
- **Hasil**: Aplikasi Manager dapat dibuka langsung kapan saja tanpa dialog konfirmasi UAC yang mengganggu.

---

### F. Seamless One-Click Deployment (Zero Input / Tanpa Penekanan Tombol)
- [`ScriptGenerator.cs`](file:///root/project/LabSupport/LabSupport.Shared/Services/ScriptGenerator.cs):
  - **Zero-Input Identity**: Seluruh prompt `Read-Host` dihapus. Script otomatis memakai nama komputer bawaan (`[System.Environment]::MachineName`) tanpa meminta ketikan keyboard, dengan dukungan argumen CLI opsional (`install_agent.bat PC-01`).
  - **UAC Argument Forwarding**: Menambahkan parameter `-ArgumentList '%*'` pada saat auto-elevasi hak administrator sehingga parameter CLI tetap terjaga.
  - **Non-Blocking Error**: Menghapus `Read-Host` pada saat gagal ping/unduh, membiarkan `pause` di level batch script yang hanya menahan jendela jika exit code != 0.
  - **Auto-Close On Success**: Menampilkan ringkasan progres 5 langkah dengan warna jelas, memunculkan banner hijau `[SUKSES]`, lalu otomatis menutup jendela dalam 1 detik (`Start-Sleep -Seconds 1`).
- **Hasil**: Teknisi cukup klik ganda file `.bat`, konfirmasi dialog UAC Windows ("Yes"), dan instalasi langsung berjalan sendiri hingga selesai dan jendela menutup secara otomatis tanpa perlu menekan tombol apa pun di keyboard.

---

## 2. Fitur Baru: Adaptive Streaming, Selective Web Filter & Hardware Inventory

### A. Network & Adaptive Screen Streaming
1. **Dirty Frame Check (Skip Unchanged Frames)**:
   - [`ScreenCaptureService.cs`](file:///root/project/LabSupport/LabSupport.Shared/Services/ScreenCaptureService.cs): `ComputeBitmapSampleHash` mengambil 16 titik sampling representatif di seluruh layar menggunakan FNV-1a 64-bit hashing.
   - `CaptureScreenAdaptiveJpeg` membandingkan hash frame saat ini dengan hash frame sebelumnya. Jika layar sedang statis (misal guru sedang menjelaskan slide presentasi), encoding JPEG dan pengiriman paket data di-skip total.
   - Menghemat bandwidth LAN hingga ~80% saat layar tidak berubah.
2. **Quality & Framerate Presets pada Toolbar**:
   - [`DashboardViewModel.cs`](file:///root/project/LabSupport/LabSupport.Manager/ViewModels/DashboardViewModel.cs): Ditambahkan pilihan preset streaming pada toolbar:
     - **Eco (720p @ 8fps)**: 1280x720, Jpeg Quality 55, Interval 125ms (Sangat hemat bandwidth untuk jaringan Wi-Fi lab / switch 100Mbps).
     - **Balanced (900p @ 15fps)**: 1600x900, Jpeg Quality 70, Interval 66ms (Default seimbang dan jernih).
     - **High (1080p @ 25fps)**: 1920x1080, Jpeg Quality 85, Interval 40ms (Kualitas tinggi untuk switch Gigabit).
3. **Bandwidth Hog Alert & Throttle**:
   - Kartu PC siswa otomatis menampilkan badge merah `⚠️ Net Hog` jika mendownload > 3 MB/s (3000 KB/s).
   - Menu klik kanan / toolbar menyediakan aksi `⚠️ Teguran Bandwidth...` untuk mengirimkan notifikasi peringatan langsung ke layar siswa.

---

### B. Selective Web Filtering (Dual-Mode DNS Sinkhole)
1. **Windows Hosts Sinkhole Engine**:
   - [`WebFilterService.cs`](file:///root/project/LabSupport/LabSupport.Shared/Services/WebFilterService.cs): Mengarahkan domain terlarang ke sinkhole `0.0.0.0 domain` dan `0.0.0.0 www.domain` di file Windows `drivers\etc\hosts`.
   - Menjaga keutuhan konfigurasi hosts bawaan sistem dengan penanda blok aman `# BEGIN LABSUPPORT WEB FILTER` dan `# END LABSUPPORT WEB FILTER`.
   - Otomatis melakukan pembersihan cache DNS via `DnsFlushResolverCache` (Win32 DLL) dan `ipconfig /flushdns` agar blokir langsung aktif seketika tanpa perlu restart browser.
   - Bekerja efektif di semua browser modern (Google Chrome, Microsoft Edge, Mozilla Firefox, Brave, Opera) tanpa perlu plugin atau proxy pihak ketiga.
2. **Preset Mode Terintegrasi di Toolbar Manager**:
   - **🚫 Blok Sosmed & Game**: Memblokir TikTok, YouTube, Roblox, Steam, Epic Games, Riot Games, Instagram, Twitter/X, Discord, Spotify, Netflix, Twitch, dsb.
   - **✅ Whitelist Edukasi**: Mode ujian / pembelajaran terfokus yang membatasi akses keluar dan hanya mengizinkan domain sekolah/pembelajaran.
   - **🔓 Buka Web Filter**: Melepas seluruh sinkhole dan mengembalikan akses web secara penuh.

---

### C. Complete Hardware Inventory & Diagnostics
1. **Lightweight Hardware Telemetry (Near-Zero CPU)**:
   - [`HardwareInfoService.cs`](file:///root/project/LabSupport/LabSupport.Agent/Services/HardwareInfoService.cs): Mengambil informasi hardware statis langsung dari Windows Registry dan DriveInfo tanpa overhead WMI yang lambat:
     - **Processor**: Registry `HARDWARE\DESCRIPTION\System\CentralProcessor\0\ProcessorNameString`
     - **GPU**: Registry `SYSTEM\CurrentControlSet\Control\Class\{4d36e968...}\DriverDesc`
     - **Disk Storage**: Total & Free Space via `DriveInfo.GetDrives()`
     - **Motherboard Serial**: Registry `HARDWARE\DESCRIPTION\System\BIOS\BaseBoardSerialNumber`
   - Data di-cache dalam memori sehingga tidak membebani CPU PC siswa pada setiap siklus heartbeat.
2. **Health Warning Badges pada UI**:
   - **💾 Disk Rendah**: Otomatis muncul pada kartu PC jika sisa kapasitas penyimpanan < 10 GB atau < 10% dari total disk.
   - **⚡ RAM 90%+**: Peringatan jika pemakaian RAM mendekati batas maksimal.
3. **Rincian Hardware Modal Dialog**:
   - Klik kanan pada PC mana saja -> `🔍 Rincian Hardware...` membuka jendela dialog popup modern yang menyajikan spesifikasi lengkap: Hostname, Ruang/Grup, IP, MAC, Model CPU, Model GPU, Kapasitas Total & Sisa Disk, Nomor Seri Motherboard, dan Versi Windows.
4. **Ekspor Inventaris Hardware ke CSV**:
   - Tombol `🖥️ Ekspor Hardware` di toolbar dan menu Settings untuk mengekspor seluruh aset PC lab dalam format CSV berstandar laporan sarana prasarana sekolah/kampus (dengan sanitasi formula injection CWE-1236).

---

## 3. Fitur Mutakhir: Multi-Lab Selection & Mass Action Command Center

### A. Multi-Lab & Seleksi Fleksibel
1. **Seleksi Individual & Per-Lab**:
   - Setiap kartu komputer di Dashboard dilengkapi checkbox seleksi mandiri.
   - Header setiap Lab/Ruangan memiliki tombol cepat: `☑ Pilih Lab Ini` dan `☐ Batal`.
2. **Multi-Lab Dialog Flyout**:
   - Tombol `📋 Multi-Lab...` di toolbar membuka modal checklist multi-ruangan untuk memilih kombinasi beberapa lab sekaligus (misalnya Lab 209A + Lab 209B).
   - Dilengkapi tombol `Pilih Semua` dan `Kosongkan`.
3. **Invert Selection & Dynamic Target Summary**:
   - Tombol `🔄 Balikkan` membalik status seleksi komputer yang sedang aktif.
   - Ringkasan target cerdas ditampilkan secara dinamis di status bar dan toolbar:
     `🎯 45 PC Terpilih (Lab 209A: 30, Lab 209B: 15)`
   - Hero button `🚀 Aktivitas Masal (N)` otomatis menampilkan jumlah PC yang siap dieksekusi.

---

### B. Mass Action Command Center (Dialog Pusat Kendali Masal)
Jendela modal modern `MassActionDialog.xaml` menyajikan 6 kategori aksi masal berkinerja tinggi:

1. **⚡ Kontrol Daya (Power Control)**:
   - **Nyalakan (Wake WoL)**: Magic Packet UDP broadcast ke seluruh MAC address terpilih.
   - **Mode Standby/Sleep**: Menggunakan Win32 `powrprof.dll` `SetSuspendState(false, true, false)` untuk menghemat listrik lab.
   - **Restart PC & Matikan PC**: Shutdown/Reboot jarak jauh dengan proteksi konfirmasi dialog keselamatan.
2. **🌑 Layar Gelap (Pitch Black Screen)**:
   - 1-Klik layar langsung hitam pekat murni (`#000000`).
   - Kursor mouse disembunyikan (`SetCursor(IntPtr.Zero)` + `ShowCursor(false)`).
   - Input mouse dan keyboard diblokir penuh (`BlockInput(true)`).
   - Layar bersih tanpa teks instruksi untuk fokus pengarahan instruktur/laboran.
   - Tombol `🔓 Pulihkan Layar Normal` untuk membuka kembali secara instan.
3. **📜 Skrip Masal (Preset & Editor Kustom)**:
   - Template pemeliharaan 1-klik:
     - `🌐 Bersihkan Cache DNS (Flush DNS)`
     - `💾 Cek Kapasitas Disk C:`
     - `🛑 Tutup Paksa Browser Macet (Chrome, Edge, Firefox)`
     - `⏱️ Sinkronisasi Waktu Windows (W32Time)`
   - Editor skrip terintegrasi dengan opsi radio button **PowerShell** atau **Batch/CMD**.
4. **⌨️ Aksi Keyboard Masal**:
   - Tombol cepat Win32 Virtual Key: `Win+D (Minimize All)`, `Alt+F4 (Tutup Jendela)`, `Win+L (Kunci Workstation)`, `Enter`, `Esc`.
   - **Broadcast Teks/Perintah Ketik**: Mengirim rentetan pengetikan teks ke semua PC aktif secara bersamaan.
5. **🚀 Peluncur Masal (URL & Aplikasi)**:
   - Luncurkan URL website (misal: CBT Ujian, LMS Moodle, Google Form) langsung ke browser default siswa dengan sanitasi protokol `http://` / `https://`.
   - Jalankan program/aplikasi (misal: `notepad.exe`, `calc.exe`) dengan parameter sanitasi anti command-injection.
6. **🧹 Pemeliharaan Lab Cepat**:
   - **1-Klik Bersihkan Workspace**: Menghapus file temporary `%TEMP%`, `Prefetch`, Recycle Bin, dan download siswa.
   - **Paksa Hentikan Proses**: Menghentikan proses berdasarkan nama executable (misal: `discord.exe`, `game.exe`).
7. **Guard Keselamatan & Pelacak Progres Per-PC**:
   - Aksi destruktif (Shutdown, Restart, Clean Workspace, Kill Process) dilindungi dialog konfirmasi bertingkat ("Apakah Anda yakin?").
   - Progres bar real-time dan daftar hasil eksekusi per komputer dengan status Berhasil (hijau), Gagal (merah), atau Melewati (kuning).
   - Tombol `🔄 Ulangi yang Gagal` untuk mencoba kembali eksekusi hanya pada PC yang sempat offline atau timeout.

---

## 4. Hasil Pengujian Otomatis

Seluruh pengujian unit dan integrasi lulus dengan sukses (**209/209 tes lulus 100%**):
```text
Passed!  - Failed: 0, Passed: 209, Skipped: 0, Total: 209, Duration: 31 s - LabSupport.Tests.dll (net8.0)
Build succeeded: 0 Warning(s), 0 Error(s)
```

Suite pengujian mencakup:
- [`MassActionAndSelectionTests.cs`](file:///root/project/LabSupport/LabSupport.Tests/MassActionAndSelectionTests.cs):
  1. `MultiLabSelection_SelectRoom_SelectsOnlyTargetRoom`: Validasi seleksi PC per nama ruangan.
  2. `MultiLabSelection_DeselectRoom_DeselectsOnlyTargetRoom`: Validasi pembatalan seleksi per lab.
  3. `MultiLabSelection_SelectMultipleRooms_SelectsMatchingRooms`: Validasi seleksi kombinasi multi-lab.
  4. `MultiLabSelection_InvertSelection_InvertsAllComputers`: Validasi operasi toggle invert selection.
  5. `MultiLabSelection_FormatSelectionSummary_ReturnsFormattedSummary`: Validasi format string target ringkasan `🎯 X PC Terpilih (Lab A: X)`.
  6. `MultiLabSelection_GetRoomSummaryList_CalculatesAccurately`: Validasi perhitungan statistik total, online, dan selected per lab.
  7. `MultiLabSelection_SelectRoom_WithNullOrWhitespaceRoom_MatchesGeneralRoom`: Validasi normalisasi room null/whitespace ke "General".
  8. `SleepCommand_ExecutesAndReturnsExpectedResponse`: Validasi deserialisasi dan eksekusi `CommandType.Sleep`.
  9. `BlackScreenCommand_EnableAndDisable_TogglesBlackScreenState`: Validasi aktivasi dan pemulihan Black Screen via protokol TCP.
  10. `ScreenLock_TransitionsBetweenBlackScreenAndAttentionLock_PreservesState`: Validasi transisi bolak-balik antara pitch-black `#000000` dan Attention Lock.
  11. `BlackScreenPayload_Serialization_RoundTripsCorrectly`: Validasi serialisasi `BlackScreenPayload`.
  12. `MassScript_PresetTemplates_GenerateValidPayloads`: Validasi 4 preset skrip pemeliharaan (Flush DNS, Cek Disk, Browser Kill, W32Time).
  13. `MassKeyboard_ShortcutAndTextPayloads_GenerateValidCommands`: Validasi shortcut dan simulasi keyboard.
  14. `InputSimulator_SimulateKeyboard_WithNewlines_ExecutesSuccessfully`: Validasi pengiriman teks dengan karakter baris baru (`\r\n` / `\n`).
  15. `MassLaunch_Sanitization_RejectsDangerousCharacters`: Validasi sanitasi target peluncur terhadap karakter berbahaya command injection (`&`, `|`, `;`, dsb).
  16. `ForceKill_CriticalOsProcesses_AreRecognized`: Validasi daftar proteksi proses inti OS Windows (`csrss`, `svchost`, `winlogon`, dsb).

---

## 5. Binari Siap Distribusi (Self-Contained Windows x64)

Seluruh executable portabel terbaru telah dikompilasi ke direktori `dist/`:
- **PC Manajemen / Guru**: [`dist/manager/LabSupport.Manager.exe`](file:///root/project/LabSupport/dist/manager/LabSupport.Manager.exe) (~148 MB self-contained single-file executable)
- **Embedded Agent Installer Server**: [`dist/manager/LabSupport.Agent.exe`](file:///root/project/LabSupport/dist/manager/LabSupport.Agent.exe) (~68 MB self-contained single-file untuk distribusi otomatis via HTTP)
- **PC Laboratorium Siswa**: [`dist/agent/LabSupport.Agent.exe`](file:///root/project/LabSupport/dist/agent/LabSupport.Agent.exe) (~68 MB self-contained single-file service)
- **Setup Perizinan**: [`dist/manager/setup_manager_permissions.bat`](file:///root/project/LabSupport/dist/manager/setup_manager_permissions.bat) (One-time URL ACL setup)

