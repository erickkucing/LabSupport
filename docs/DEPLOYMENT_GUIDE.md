# Panduan Deployment & Instalasi LabSupport

Panduan langkah demi langkah untuk menginstalasi dan mengoperasikan **LabSupport** di lingkungan laboratorium komputer sekolah, universitas, atau institusi pelatihan.

---

## 📋 Pra-Syarat Sistem

| Perangkat | Sistem Operasi | Kebutuhan Minimum |
|---|---|---|
| **PC Guru / Laboran (Manager)** | Windows 10 / 11 (64-bit) | RAM 4 GB, Layar 1366x768 atau lebih tinggi, Port LAN gigabit / Wi-Fi |
| **PC Siswa / Klien (Agent)** | Windows 7 SP1 / 8.1 / 10 / 11 (64-bit) | RAM 2 GB, Terhubung ke jaringan LAN yang sama |

> [!NOTE]
> **Tidak perlu instalasi .NET Framework / Runtime eksternal**: Seluruh binary LabSupport telah dikompilasi secara *self-contained single-file* dengan runtime .NET 8 terintegrasi di dalamnya.

---

## 🚀 Langkah 1: Pemasangan di PC Laboran (Manager)

1. **Unduh Paket Manager**:
   - Unduh berkas `LabSupport-Manager-v1.0.0.zip` dari [Halaman Rilis GitHub](https://github.com/erickkucing/LabSupport/releases/latest).
2. **Ekstrak Berkas**:
   - Ekstrak isi ZIP ke folder yang aman (misalnya: `C:\LabSupportManager` atau langsung di flash disk portabel).
3. **Konfigurasi Hak Akses Jaringan (Cukup 1x Saja)**:
   - Klik kanan pada `setup_manager_permissions.bat` dan pilih **Run as administrator**.
   - Skrip ini secara otomatis:
     - Mendaftarkan reservasi URL ACL Windows HTTP API untuk port `8080` (mencegah error *Access Denied*).
     - Membuka firewall Windows untuk TCP port `8080` dan UDP port `5903` (Discovery Beacon).
4. **Jalankan Aplikasi**:
   - Klik ganda `LabSupport.Manager.exe`.
   - Dashboard akan terbuka dan server HTTP siap menerima koneksi client.

---

## 💻 Langkah 2: Pemasangan di PC Laboratorium (Agent)

Ada dua metode yang dapat digunakan:

### Metode A: Menggunakan Generator Bawaan Manager (Paling Direkomendasikan)

1. Buka aplikasi `LabSupport.Manager.exe`.
2. Klik tab menu **Generate Agent** di sidebar sebelah kiri.
3. IP address PC Manager akan terdeteksi secara otomatis (misal: `192.168.1.100`). Jika PC memiliki beberapa adapter jaringan, pilih IP subnet lab yang aktif.
4. Klik tombol **Generate Paket Agent**.
5. Salin folder hasil generate ke flash disk USB. Di dalamnya terdapat:
   - `install_agent.bat` (Launcher auto-elevasi hak Administrator)
   - `install_agent.ps1` (Skrip installer pintar)
   - `LabSupport.Agent.exe` (Headless binary)
6. Tancapkan flash disk ke PC Siswa, lalu **klik ganda `install_agent.bat`**.
7. Skrip akan otomatis:
   - Menguji koneksi **ping** ke PC Manager (memastikan jaringan terhubung).
   - Memasang file ke `C:\LabSupportAgent\`.
   - Mengonfigurasi firewall untuk port `5901` dan `5902`.
   - Mendaftarkan Windows Service `LabSupportAgent` dengan startup tipe *Automatic*.
   - Menjalankan service seketika di latar belakang.
8. Dalam hitungan 5 detik, komputer lab tersebut akan langsung muncul di Dashboard Manager dengan kartu status **Online (Hijau)**.

---

### Metode B: Distribusi Masal Jaringan (Active Directory GPO / PDQ Deploy / Script)

Jika laboratorium memiliki domain controller atau tool deploy, Anda dapat mendistribusikan agent dengan parameter hening (*silent*):

```cmd
:: Eksekusi silent install via command prompt / batch admin
powershell.exe -ExecutionPolicy Bypass -File "\\SERVER\Share\install_agent.ps1" -ManagerIp "192.168.1.100" -ManagerPort 8080
```

---

## ⚡ Konfigurasi Fitur Wake-on-LAN (WoL)

Agar PC Manager dapat menyalakan komputer lab yang dalam kondisi mati total:

1. **Pengaturan BIOS/UEFI di PC Siswa**:
   - Masuk ke BIOS setup saat boot (tekan `DEL` / `F2` / `F12`).
   - Cari menu **Power Management** atau **Advanced**.
   - Aktifkan opsi: **Wake on LAN**, **Power On by PCI-E/LAN**, atau **ACPI Wake from S5**.
   - Nonaktifkan **Deep Sleep / ErP / EuP** (karena mematikan daya pada kartu jaringan saat off).
2. **Pengaturan Driver LAN di Windows**:
   - Buka `Device Manager` ➔ `Network Adapters` ➔ Klik kanan adapter LAN fisik ➔ `Properties`.
   - Pada tab **Power Management**, centang:
     - *Allow this device to wake the computer*
     - *Only allow a magic packet to wake the computer*
   - Pada tab **Advanced**, pastikan **Wake on Magic Packet** bernilai `Enabled`.
3. Alamat fisik (MAC Address) PC Siswa akan terekam secara otomatis di database SQLite Manager saat komputer pertama kali online.

---

## 🛠️ Pemecahan Masalah (Troubleshooting)

### 1. Komputer Siswa Tidak Muncul di Dashboard
- **Uji Ping**: Buka CMD di PC Siswa, ketik `ping <IP-PC-Manager>`. Jika *Request Timed Out*, periksa kabel LAN, Wi-Fi, atau isolasi AP pada router.
- **Firewall PC Manager**: Pastikan script `setup_manager_permissions.bat` telah dijalankan di PC Manager untuk mengizinkan port `8080` dan `5903`.
- **Cek Status Service**: Di PC Siswa, buka PowerShell dan ketik:
  ```powershell
  Get-Service LabSupportAgent
  ```
  Pastikan statusnya adalah `Running`. Jika `Stopped`, jalankan `Start-Service LabSupportAgent`.

### 2. Live Screen Monitoring Hitam atau Tidak Tampil
- Port streaming `5901` pada PC Siswa terblokir firewall pihak ketiga (seperti antivirus bawaan Avast/Kaspersky/Norton).
- Tambahkan izin pengecualian untuk file `C:\LabSupportAgent\LabSupport.Agent.exe` pada firewall antivirus terkait.

### 3. IP PC Guru Berubah (DHCP Reset)
- Anda **tidak perlu menginstal ulang agent**. Fitur *Zero-Touch Self-Healing* LabSupport memancarkan beacon UDP port `5903`.
- Dalam waktu maksimal 10 detik, seluruh PC Siswa akan mengenali IP baru Manager secara otomatis dan menyambungkan kembali koneksinya.
