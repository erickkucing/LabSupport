# Arsitektur & Spesifikasi Protokol LabSupport

Dokumen ini menjelaskan arsitektur internal, topologi jaringan, spesifikasi protokol komunikasi, dan alur data dari sistem manajemen laboratorium komputer **LabSupport**.

---

## 1. Topologi Jaringan & Komponen

```mermaid
graph TD
    subgraph ManagerHost ["🖥️ PC Guru / Laboran (Manager)"]
        UI["WPF MVVM GUI (Fluent UX)"]
        HttpSrv["HTTP File Server (:8080)"]
        Beacon["UDP Beacon Broadcaster (:5903)"]
        CmdClient["TCP Command Client"]
        MjpegClient["MJPEG Stream Client"]
        DB[(SQLite WAL Engine)]
    end

    subgraph Network ["LAN / Wi-Fi Switching Network"]
        direction TB
        HttpLink["HTTP Traffic (:8080)"]
        BeaconLink["UDP Discovery (:5903)"]
        CmdLink["TCP Commands (:5902)"]
        MjpegLink["MJPEG Stream (:5901)"]
        WolLink["UDP Broadcast (:9)"]
    end

    subgraph AgentHost ["💻 PC Laboratorium / Siswa (Agent)"]
        AgentSvc["LabSupportAgent Windows Service"]
        HeartbeatWorker["Heartbeat & Screenshot Worker"]
        DiscoveryClient["UDP Discovery Listener"]
        CmdListener["TCP Command Listener (:5902)"]
        StreamServer["MJPEG Streamer (:5901)"]
        LockGuard["Exam Lockdown & Process Guard"]
        Cleaner["Workspace Cleaner Service"]
    end

    UI --> DB
    UI --> CmdClient
    UI --> MjpegClient
    HttpSrv --> DB

    Beacon -.->|UDP Broadcast| BeaconLink
    BeaconLink -.-> DiscoveryClient

    HeartbeatWorker -->|POST Heartbeat| HttpLink
    HttpLink --> HttpSrv

    CmdClient -->|Send Command| CmdLink
    CmdLink --> CmdListener

    StreamServer -->|Stream Frames| MjpegLink
    MjpegLink --> MjpegClient

    CmdClient -.->|Magic Packet| WolLink
```

---

## 2. Port Jaringan & Rincian Protokol

| Port | Transport | Arah Aliran | Deskripsi & Fungsi |
|---|---|---|---|
| **8080** | HTTP / TCP | Agent ➔ Manager | Health check, pengiriman Heartbeat (JSON + JPEG thumbnail Base64), unduhan installer Agent, dan distribusi file. |
| **5901** | HTTP / TCP | Manager ➔ Agent | Server streaming visual MJPEG langsung (*real-time screen monitoring* & *presentation broadcast*). |
| **5902** | TCP Socket | Manager ➔ Agent | Saluran eksekusi perintah terenkripsi ringan: Remote script (PowerShell/CMD), Shutdown, Restart, Lock Screen, USB Block, Audio Control, dan Lockdown Ujian. |
| **5903** | UDP Broadcast | Dua Arah | **Zero-Touch Auto Discovery & Dynamic IP Self-Healing**: Mengizinkan Agent mendeteksi perubahan IP Address PC Manager tanpa rekonfigurasi manual. |
| **9 / 7** | UDP Broadcast | Manager ➔ Subnet | Paket Magic Packet Wake-on-LAN (102 bytes) untuk menghidupkan komputer yang mati via kartu jaringan LAN. |

---

## 3. Spesifikasi Protokol Komunikasi

### A. Heartbeat Payload (Agent ➔ Manager via HTTP POST `http://<manager-ip>:8080/api/heartbeat`)

```json
{
  "computerName": "LAB-PC-01",
  "ipAddress": "192.168.1.101",
  "macAddress": "00:1A:2B:3C:4D:5E",
  "osVersion": "Microsoft Windows 11 Pro 10.0.22631",
  "cpuUsage": 14.5,
  "memoryUsage": 42.8,
  "status": "Online",
  "currentScreenBase64": "/9j/4AAQSkZJRg...",
  "activeUser": "student_01",
  "lastSeen": "2026-10-28T08:30:00Z"
}
```

- **Frekuensi Transmisi**: Setiap 5 detik sekali.
- **Throttling Database**: Manager menyimpan thumbnail di cache memori, dan hanya menulis ke disk SQLite jika IP/MAC berubah atau setiap interval flush 60 detik (menghemat disk I/O sebesar 95%).

### B. TCP Command Payload (Manager ➔ Agent via Port 5902)

Setiap frame perintah diawali dengan header panjang 4-byte *Big-Endian Integer* yang menunjukkan panjang payload UTF-8 JSON berikutnya:

```json
{
  "commandId": "b1a2c3d4-e5f6-7890-abcd-ef1234567890",
  "type": 3,
  "payload": "powershell.exe -NoProfile -Command \"Get-Process | Select-Object -First 5\"",
  "timeoutSeconds": 30
}
```

#### Tipe Perintah (`CommandType` Enum):
- `0` : **Shutdown** (Inisiasi *InitiateSystemShutdownEx*)
- `1` : **Restart** (Inisiasi *ExitWindowsEx*)
- `2` : **Lock** (Kunci sesi Windows via *LockWorkStation*)
- `3` : **ExecuteScript** (PowerShell / Batch asynchronously dengan batas timeout)
- `4` : **LockdownExam** (Aktifkan Exam Guard & blokir aplikasi non-ujian)
- `5` : **UnlockExam** (Pulihkan akses desktop normal)
- `6` : **BlockUsb** / **UnblockUsb** (Manipulasi registry *USBSTOR* start value)
- `7` : **FileTransfer** (Unduh file dari HTTP Manager ke direktori tujuan lokal)
- `8` : **SetAudioVolume** (Atur volume audio sistem atau *Mute*)
- `9` : **InjectClipboard** (Salin teks materi/link ke clipboard komputer siswa)

---

## 4. Mekanisme Zero-Touch Self-Healing (Perubahan IP Dinamis)

Dalam lingkungan sekolah atau kampus, PC Laboran/Guru sering kali mendapatkan IP baru dari DHCP saat router reboot atau berpindah subnet.

```mermaid
sequenceDiagram
    participant M as PC Manager (IP Baru: 192.168.1.150)
    participant B as UDP Broadcaster (Port 5903)
    participant A as PC Agent (Client)

    Note over M,A: Router mereset IP Manager dari .100 ke .150
    A->>M: POST /api/heartbeat ke IP lama (.100) -> GAGAL (Timeout)
    Note over A: Heartbeat gagal >= 2 kali berturut-turut
    A->>A: Masuk mode "Reconnection & Discovery"
    M->>B: Broadcast Beacon berkala tiap 5 detik: {"server":"LabSupport","ip":"192.168.1.150"}
    A->>B: Tangkap Beacon atau kirim UDP "FindManager"
    B-->>A: Respons konfirmasi IP Manager aktif
    A->>A: Update memori & tulis appsettings.json lokal
    A->>M: POST /api/heartbeat ke IP baru (.150) -> SUKSES
    Note over M,A: Komputer lab langsung kembali hijau (Online)
```

---

## 5. Keamanan & Pencegahan Gangguan

1. **UAC Elevation**: Script pemasangan (`install_agent.bat`) menyertakan manifest auto-elevasi Administrator dengan fallback PowerShell prompt.
2. **Crash-Proof Worker**: Agen berjalan sebagai Windows Service di bawah akun `LocalSystem` dengan Scheduled Task recovery pada logon sesi pengguna.
3. **Sandbox Script Execution**: Script yang dikirimkan dijalankan pada thread terpisah dengan proses *timeout watcher* untuk mencegah proses menggantung (*hung processes*).
