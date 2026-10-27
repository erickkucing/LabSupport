# LabSupport — Portable Computer Laboratory Management System
> Sistem Manajemen Laboratorium Komputer Berbasis Windows Native (100% Portable Tanpa Instalasi)

LabSupport adalah perangkat lunak manajemen dan remote laboratorium komputer native Windows yang dirancang sebagai alternatif modern, ringan, dan portabel untuk perangkat lunak seperti NetSupport School. Aplikasi ini dapat dijalankan langsung (portable .exe) dari USB flash drive tanpa instalasi rumit, baik di sisi PC Laboran (Manajemen) maupun di sisi PC Laboratorium (Client/Agent).

---

## Arsitektur Sistem

```
┌────────────────────────────────────────────────────────┐
│     🖥️ PC Manajemen (LabSupport.Manager - WPF GUI)     │
├────────────────────────────────────────────────────────┤
│ • Dashboard Grid PC Lab (Live Thumbnail, CPU, RAM)    │
│ • Remote Desktop Streaming & Screen Monitoring         │
│ • Multi-PC Broadcast Script Runner (PowerShell / CMD)  │
│ • Power Manager: Shutdown, Restart, Lock, Wake-on-LAN  │
│ • Automated Agent Script Generator (.bat / .ps1)      │
│ • Built-in HTTP Server (Port 8080) & SQLite DB         │
└───────────────────────┬────────────────────────────────┘
                        │
       LAN / Wi-Fi Network Connection (Ping Verifikasi)
                        │
┌───────────────────────▼────────────────────────────────┐
│      💻 PC Lab (LabSupport.Agent - Headless Service)    │
├────────────────────────────────────────────────────────┤
│ • Headless Windows Service (Autostart Otomatis)        │
│ • Push Heartbeat & Screenshot Thumbnail (Tiap 5 Detik) │
│ • MJPEG Live Screen Streamer (Port 5901)               │
│ • TCP Command Listener (Port 5902)                     │
│ • Script Executor (PowerShell & Batch Async Engine)    │
│ • Win32 Power Controller & MAC Address Broadcaster     │
└────────────────────────────────────────────────────────┘
```

---

## Fitur Utama

1. **100% Portable (Self-Contained Executable)**
   - Tidak memerlukan instalasi runtime .NET tambahan di PC target (runtime telah ter-embed).
   - Cukup salin folder atau jalankan `.exe` langsung dari flash disk.

2. **Dashboard Interaktif Real-time**
   - Tampilan grid responsif menampilkan thumbnail layar komputer, hostname, IP, MAC address, serta penggunaan CPU dan RAM terkini.
   - Indikator status Online (hijau) dan Offline (abu-abu) otomatis via heartbeat detector (timeout 15 detik).
   - Pencarian instan dan penyaringan status (Semua, Online, Offline).

3. **Remote Desktop Interaktif & Screen Monitoring**
   - Streaming tampilan layar PC Lab secara langsung melalui protokol MJPEG ringan.
   - **Kontrol Mouse & Keyboard Penuh**: Klik kiri, klik kanan, klik ganda, scroll mouse, dan kirim pengetikan teks secara real-time.
   - Tombol shortcut cepat: Win+D (Desktop), Alt+F4, Task Manager (Ctrl+Shift+Esc), Kunci Layar (LockWorkStation), Restart, Shutdown, dan Simpan Screenshot PNG.

4. **Broadcast Layar (Mode Presentasi Guru/Laboran)**
   - Streaming tampilan layar PC Manajemen ke seluruh atau sebagian PC Lab secara real-time.
   - PC Lab otomatis membuka layar presentasi secara fullscreen (kiosk borderless) dan dapat dihentikan kapan saja dari Manager.

5. **Transfer File Jarak Jauh (File Transfer)**
   - Kirim file materi, tugas, atau installer dari PC Manajemen ke PC Lab secara individual maupun broadcast ke banyak PC sekaligus.

6. **Eksekusi Script Multi-PC (Broadcast Command)**
   - Jalankan script PowerShell atau Batch/CMD ke seluruh komputer lab atau komputer terpilih secara bersamaan.
   - Dilengkapi template cepat: Info Sistem, Flush DNS, Daftar Layanan Aktif, Pembersihan File Temporary, dan Ping Test.
   - Hasil output (stdout, stderr, dan waktu eksekusi) ditampilkan per PC secara transparan di panel kontrol.

7. **Manajemen Daya Lengkap**
   - **Shutdown**: Mematikan komputer lab dari jarak jauh secara aman.
   - **Restart**: Merestart komputer lab.
   - **Lock Screen**: Mengunci workstation seketika untuk menjaga fokus siswa/praktikan.
   - **Wake-on-LAN (WoL)**: Menyalakan komputer yang mati melalui pengiriman Magic Packet UDP ke port 9 dan 7 ke alamat MAC fisik.

8. **Deploy Headless Agent Otomatis (Generator Script)**
   - Menu **Generate Agent** di Manager mendeteksi IP address LAN secara otomatis.
   - Menghasilkan dua file installer siap pakai:
     - `install_agent.bat`: Launcher pintar dengan auto-elevasi hak akses Administrator (UAC prompt).
     - `install_agent.ps1`: Memeriksa ping ke PC Manajemen terlebih dahulu (wajib), mengunduh binary agent berkecepatan tinggi, mengonfigurasi Windows Firewall untuk port 5901 & 5902, mendaftarkan Windows Service `LabSupportAgent` (Automatic) serta Scheduled Task (OnLogon highest privilege), dan langsung menjalankannya secara headless.

9. **Database SQLite & Riwayat Aktivitas**
   - Menyimpan daftar komputer, catatan inventaris, alamat MAC untuk Wake-on-LAN, dan riwayat aktivitas perintah secara lokal tanpa perlu konfigurasi database eksternal.

10. **Dukungan Bilingual**
    - Mendukung peralihan bahasa antarmuka secara instan antara **Bahasa Indonesia** dan **English**.

---

## Struktur Proyek

```
LabSupport/
├── LabSupport.Shared/           # Protokol, model data, SQLite service, dan HTTP/TCP helpers
│   ├── Models/                 # ComputerInfo, ActivityLog, AppSettings
│   ├── Protocol/               # HeartbeatMessage, CommandMessage, WolHelper, Serializer
│   └── Services/               # DatabaseService, HttpFileServer, AgentCommandService, ScriptGenerator, WolService
│
├── LabSupport.Agent/            # Windows Service headless untuk PC Lab
│   ├── Services/               # ScreenCaptureService, MjpegStreamer, CommandListener, ScriptExecutor, PowerManager, HeartbeatService
│   ├── Worker.cs               # BackgroundService coordinator
│   └── Program.cs              # Service host & configuration entry point
│
├── LabSupport.Manager/          # Aplikasi Desktop WPF untuk PC Laboran
│   ├── Models/                 # LabComputer (WPF Observable model)
│   ├── ViewModels/             # MainViewModel, DashboardViewModel, ScriptViewModel, RemoteViewModel, GenerateAgentViewModel, SettingsViewModel
│   ├── Views/                  # DashboardView, ScriptView, RemoteView, GenerateAgentView, SettingsView
│   ├── Resources/Localization/ # Strings.id-ID.xaml, Strings.en-US.xaml
│   └── MainWindow.xaml         # Windows Fluent layout & sidebar navigation
│
├── LabSupport.Tests/            # Suite unit & integration test
│   ├── ProtocolTests.cs        # Verifikasi serialisasi protokol
│   ├── ScriptGeneratorTests.cs # Verifikasi logika script ping & deploy
│   ├── WolTests.cs             # Verifikasi pembentukan paket Wake-on-LAN 102 bytes
│   ├── DatabaseTests.cs        # Verifikasi operasi CRUD SQLite
│   ├── HttpServerTests.cs      # Verifikasi HTTP health, heartbeat, dan download
│   ├── AgentCommandTests.cs    # Verifikasi komunikasi TCP command/response
│   └── ScriptExecutorTests.cs  # Verifikasi eksekusi script dan penanganan timeout
│
└── dist/                        # Binary portable siap distribusi (win-x64)
    ├── agent/                  # LabSupport.Agent.exe
    └── manager/                # LabSupport.Manager.exe + LabSupport.Agent.exe
```

---

## Panduan Penggunaan

### 1. Menjalankan PC Manajemen
1. Salin folder `dist/manager` ke PC Laboran atau jalankan langsung dari USB flash drive.
2. Buka `LabSupport.Manager.exe`.
3. Server HTTP otomatis aktif di port 8080 dan siap menerima koneksi client.

### 2. Memasang Agent di PC Laboratorium
1. Pada aplikasi Manager, klik menu **Generate Agent**.
2. Pilih IP Address LAN PC Manajemen (terdeteksi otomatis), lalu klik **Generate Paket Agent**.
3. Salin folder hasil generate (`install_agent.bat`, `install_agent.ps1`, dan `LabSupport.Agent.exe`) ke USB flash drive.
4. Colokkan USB ke PC Laboratorium dan klik ganda `install_agent.bat`.
5. Script akan:
   - Meminta hak Administrator jika belum aktif.
   - Melakukan uji **ping** ke PC Manajemen untuk memverifikasi sambungan jaringan.
   - Mengunduh binary agent (atau menyalin dari USB) ke `C:\LabSupportAgent`.
   - Mendaftarkan Windows Service `LabSupportAgent` dengan startup *Automatic*.
   - Menjalankan service di latar belakang secara headless.
6. Komputer lab akan langsung muncul di Dashboard PC Manajemen dalam hitungan detik.

### 3. Mengontrol Komputer Lab
- **Remote Desktop**: Klik tombol **Remote** pada kartu komputer di Dashboard untuk memantau layar secara langsung.
- **Kirim Script**: Buka tab **Eksekusi Script**, pilih template atau ketik script kustom, lalu klik **Jalankan Script**.
- **Matikan / Nyalakan**: Gunakan tombol **Matikan PC** atau **Nyalakan (WoL)** pada komputer yang dipilih.

---

## Port Jaringan yang Digunakan

| Port | Arah | Protokol | Fungsi |
|------|------|----------|--------|
| **8080** | PC Lab ➔ PC Manajemen | HTTP (TCP) | Cek ping kesehatan, download binary agent, kirim heartbeat & screenshot |
| **5901** | PC Manajemen ➔ PC Lab | HTTP (TCP) | Streaming layar MJPEG untuk remote control |
| **5902** | PC Manajemen ➔ PC Lab | TCP Socket | Pengiriman perintah eksekusi script, shutdown, restart, lock screen |
| **9**    | PC Manajemen ➔ Subnet | UDP Broadcast | Magic Packet Wake-on-LAN untuk menyalakan PC |

---

## Build & Testing

### Build Seluruh Solusi
```bash
dotnet build LabSupport.sln
```

### Menjalankan Unit & Integration Tests
```bash
dotnet test
```

### Publish Portable Executables (Self-Contained Single File)
```bash
# Agent (PC Lab)
dotnet publish LabSupport.Agent -r win-x64 --self-contained true /p:PublishSingleFile=true -c Release -o ./dist/agent

# Manager (PC Laboran)
dotnet publish LabSupport.Manager -r win-x64 --self-contained true /p:PublishSingleFile=true -c Release -o ./dist/manager
```
