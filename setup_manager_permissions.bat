@echo off
:: LabSupport - One-time Windows URL ACL & Firewall Setup
:: Run this once as Administrator to allow LabSupport Manager to bind http://*:8080/ without UAC prompts
echo ============================================================
echo   LabSupport Manager - One-Time Network Permission Setup
echo ============================================================
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [INFO] Meminta hak Administrator...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo [1/2] Menambahkan reservasi URL ACL untuk Port 8080 (Wildcard)...
netsh http add urlacl url=http://*:8080/ user=Everyone >nul 2>&1
if %errorlevel% neq 0 (
    netsh http add urlacl url=http://*:8080/ user="Authenticated Users" >nul 2>&1
)

echo [2/2] Mengonfigurasi Windows Firewall Inbound untuk Port 8080 dan 5903 (UDP Beacon)...
netsh advfirewall firewall add rule name="LabSupport Manager HTTP" dir=in action=allow protocol=TCP localport=8080 enable=yes profile=any >nul 2>&1
netsh advfirewall firewall add rule name="LabSupport Manager Discovery" dir=in action=allow protocol=UDP localport=5903 enable=yes profile=any >nul 2>&1

echo ============================================================
echo [SUKSES] Konfigurasi perizinan jaringan selesai!
echo LabSupport Manager kini dapat dibuka langsung tanpa UAC berulang.
echo ============================================================
pause
