using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using LabSupport.Shared.Models;

namespace LabSupport.Shared.Services;

public class DatabaseService : IDisposable
{
    private readonly string _connectionString;

    public DatabaseService(string dbPath = "labsupport.db")
    {
        string fullPath = Path.GetFullPath(dbPath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        try
        {
            using var pragmaCmd = new SqliteCommand(@"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA busy_timeout = 5000;
            ", conn);
            await pragmaCmd.ExecuteNonQueryAsync();
        }
        catch { }

        string sql = @"
            CREATE TABLE IF NOT EXISTS computers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                hostname TEXT UNIQUE NOT NULL,
                ip_address TEXT NOT NULL,
                mac_address TEXT,
                os_version TEXT,
                last_seen TEXT NOT NULL,
                notes TEXT,
                group_name TEXT DEFAULT 'Default',
                pos_x REAL DEFAULT 0,
                pos_y REAL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_computers_mac ON computers(mac_address);

            CREATE TABLE IF NOT EXISTS activity_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                target_host TEXT NOT NULL,
                action TEXT NOT NULL,
                result TEXT,
                is_success INTEGER NOT NULL,
                timestamp TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS lab_rooms (
                name TEXT PRIMARY KEY NOT NULL,
                capacity INTEGER NOT NULL DEFAULT 30,
                description TEXT
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS pending_deletions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                hostname TEXT NOT NULL,
                mac_address TEXT,
                manager_id TEXT,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_pending_del_mac ON pending_deletions(mac_address);
            CREATE INDEX IF NOT EXISTS idx_pending_del_host ON pending_deletions(hostname);
        ";

        using var cmd = new SqliteCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();

        // Ensure group_name, room_name, display_name, pos_x, and pos_y columns exist for pre-existing databases
        try
        {
            var checkColCmd = new SqliteCommand("PRAGMA table_info(computers);", conn);
            using var reader = await checkColCmd.ExecuteReaderAsync();
            bool hasGroupCol = false;
            bool hasRoomCol = false;
            bool hasDisplayCol = false;
            bool hasPosX = false;
            bool hasPosY = false;
            bool hasCpuModel = false;
            bool hasGpuModel = false;
            bool hasDiskTotal = false;
            bool hasMbSerial = false;
            while (await reader.ReadAsync())
            {
                string colName = reader.GetString(1);
                if (colName.Equals("group_name", StringComparison.OrdinalIgnoreCase)) hasGroupCol = true;
                if (colName.Equals("room_name", StringComparison.OrdinalIgnoreCase)) hasRoomCol = true;
                if (colName.Equals("display_name", StringComparison.OrdinalIgnoreCase)) hasDisplayCol = true;
                if (colName.Equals("pos_x", StringComparison.OrdinalIgnoreCase)) hasPosX = true;
                if (colName.Equals("pos_y", StringComparison.OrdinalIgnoreCase)) hasPosY = true;
                if (colName.Equals("cpu_model", StringComparison.OrdinalIgnoreCase)) hasCpuModel = true;
                if (colName.Equals("gpu_model", StringComparison.OrdinalIgnoreCase)) hasGpuModel = true;
                if (colName.Equals("disk_total_gb", StringComparison.OrdinalIgnoreCase)) hasDiskTotal = true;
                if (colName.Equals("motherboard_serial", StringComparison.OrdinalIgnoreCase)) hasMbSerial = true;
            }
            if (!hasGroupCol)
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE computers ADD COLUMN group_name TEXT DEFAULT 'Default';", conn);
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!hasRoomCol)
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE computers ADD COLUMN room_name TEXT DEFAULT 'General';", conn);
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!hasDisplayCol)
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE computers ADD COLUMN display_name TEXT;", conn);
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!hasPosX)
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE computers ADD COLUMN pos_x REAL DEFAULT 0;", conn);
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!hasPosY)
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE computers ADD COLUMN pos_y REAL DEFAULT 0;", conn);
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!hasCpuModel)
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE computers ADD COLUMN cpu_model TEXT;", conn);
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!hasGpuModel)
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE computers ADD COLUMN gpu_model TEXT;", conn);
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!hasDiskTotal)
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE computers ADD COLUMN disk_total_gb REAL DEFAULT 0;", conn);
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!hasMbSerial)
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE computers ADD COLUMN motherboard_serial TEXT;", conn);
                await alterCmd.ExecuteNonQueryAsync();
            }
        }
        catch { }
    }

    public async Task UpsertComputerAsync(ComputerInfo comp)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        if (!string.IsNullOrWhiteSpace(comp.MacAddress))
        {
            string updateMacSql = @"
                UPDATE computers SET
                    hostname = @hostname,
                    ip_address = @ip,
                    os_version = @os,
                    last_seen = @last_seen,
                    group_name = CASE WHEN @group IS NOT NULL AND @group != '' AND @group != 'Default' THEN @group ELSE computers.group_name END,
                    room_name = CASE WHEN @room IS NOT NULL AND @room != '' AND @room != 'General' THEN @room ELSE computers.room_name END,
                    display_name = CASE WHEN @display_name IS NOT NULL AND @display_name != '' THEN @display_name ELSE computers.display_name END,
                    cpu_model = CASE WHEN @cpu_model IS NOT NULL AND @cpu_model != '' THEN @cpu_model ELSE computers.cpu_model END,
                    gpu_model = CASE WHEN @gpu_model IS NOT NULL AND @gpu_model != '' THEN @gpu_model ELSE computers.gpu_model END,
                    disk_total_gb = CASE WHEN @disk_total_gb > 0 THEN @disk_total_gb ELSE computers.disk_total_gb END,
                    motherboard_serial = CASE WHEN @motherboard_serial IS NOT NULL AND @motherboard_serial != '' THEN @motherboard_serial ELSE computers.motherboard_serial END,
                    pos_x = CASE WHEN @pos_x > 0 THEN @pos_x ELSE computers.pos_x END,
                    pos_y = CASE WHEN @pos_y > 0 THEN @pos_y ELSE computers.pos_y END
                WHERE mac_address = @mac;";
            using var updateMacCmd = new SqliteCommand(updateMacSql, conn);
            updateMacCmd.Parameters.AddWithValue("@hostname", comp.Hostname);
            updateMacCmd.Parameters.AddWithValue("@ip", comp.IpAddress);
            updateMacCmd.Parameters.AddWithValue("@mac", comp.MacAddress);
            updateMacCmd.Parameters.AddWithValue("@os", comp.OsVersion ?? string.Empty);
            updateMacCmd.Parameters.AddWithValue("@last_seen", comp.LastSeen.ToString("o"));
            updateMacCmd.Parameters.AddWithValue("@group", string.IsNullOrWhiteSpace(comp.GroupName) ? "Default" : comp.GroupName);
            updateMacCmd.Parameters.AddWithValue("@room", string.IsNullOrWhiteSpace(comp.RoomName) ? "General" : comp.RoomName);
            updateMacCmd.Parameters.AddWithValue("@display_name", comp.DisplayName ?? string.Empty);
            updateMacCmd.Parameters.AddWithValue("@cpu_model", comp.CpuModel ?? string.Empty);
            updateMacCmd.Parameters.AddWithValue("@gpu_model", comp.GpuModel ?? string.Empty);
            updateMacCmd.Parameters.AddWithValue("@disk_total_gb", comp.DiskTotalGb);
            updateMacCmd.Parameters.AddWithValue("@motherboard_serial", comp.MotherboardSerial ?? string.Empty);
            updateMacCmd.Parameters.AddWithValue("@pos_x", comp.PositionX);
            updateMacCmd.Parameters.AddWithValue("@pos_y", comp.PositionY);

            int rows = await updateMacCmd.ExecuteNonQueryAsync();
            if (rows > 0) return;
        }

        string sql = @"
            INSERT INTO computers (hostname, ip_address, mac_address, os_version, last_seen, notes, group_name, room_name, display_name, cpu_model, gpu_model, disk_total_gb, motherboard_serial, pos_x, pos_y)
            VALUES (@hostname, @ip, @mac, @os, @last_seen, @notes, @group, @room, @display_name, @cpu_model, @gpu_model, @disk_total_gb, @motherboard_serial, @pos_x, @pos_y)
            ON CONFLICT(hostname) DO UPDATE SET
                ip_address = excluded.ip_address,
                mac_address = CASE WHEN excluded.mac_address IS NOT NULL AND excluded.mac_address != '' THEN excluded.mac_address ELSE computers.mac_address END,
                os_version = excluded.os_version,
                last_seen = excluded.last_seen,
                group_name = CASE WHEN excluded.group_name IS NOT NULL AND excluded.group_name != '' AND excluded.group_name != 'Default' THEN excluded.group_name ELSE computers.group_name END,
                room_name = CASE WHEN excluded.room_name IS NOT NULL AND excluded.room_name != '' AND excluded.room_name != 'General' THEN excluded.room_name ELSE computers.room_name END,
                display_name = CASE WHEN excluded.display_name IS NOT NULL AND excluded.display_name != '' THEN excluded.display_name ELSE computers.display_name END,
                cpu_model = CASE WHEN excluded.cpu_model IS NOT NULL AND excluded.cpu_model != '' THEN excluded.cpu_model ELSE computers.cpu_model END,
                gpu_model = CASE WHEN excluded.gpu_model IS NOT NULL AND excluded.gpu_model != '' THEN excluded.gpu_model ELSE computers.gpu_model END,
                disk_total_gb = CASE WHEN excluded.disk_total_gb > 0 THEN excluded.disk_total_gb ELSE computers.disk_total_gb END,
                motherboard_serial = CASE WHEN excluded.motherboard_serial IS NOT NULL AND excluded.motherboard_serial != '' THEN excluded.motherboard_serial ELSE computers.motherboard_serial END,
                pos_x = CASE WHEN excluded.pos_x > 0 THEN excluded.pos_x ELSE computers.pos_x END,
                pos_y = CASE WHEN excluded.pos_y > 0 THEN excluded.pos_y ELSE computers.pos_y END;
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@hostname", comp.Hostname);
        cmd.Parameters.AddWithValue("@ip", comp.IpAddress);
        cmd.Parameters.AddWithValue("@mac", comp.MacAddress ?? string.Empty);
        cmd.Parameters.AddWithValue("@os", comp.OsVersion ?? string.Empty);
        cmd.Parameters.AddWithValue("@last_seen", comp.LastSeen.ToString("o"));
        cmd.Parameters.AddWithValue("@notes", comp.Notes ?? string.Empty);
        cmd.Parameters.AddWithValue("@group", string.IsNullOrWhiteSpace(comp.GroupName) ? "Default" : comp.GroupName);
        cmd.Parameters.AddWithValue("@room", string.IsNullOrWhiteSpace(comp.RoomName) ? "General" : comp.RoomName);
        cmd.Parameters.AddWithValue("@display_name", comp.DisplayName ?? string.Empty);
        cmd.Parameters.AddWithValue("@cpu_model", comp.CpuModel ?? string.Empty);
        cmd.Parameters.AddWithValue("@gpu_model", comp.GpuModel ?? string.Empty);
        cmd.Parameters.AddWithValue("@disk_total_gb", comp.DiskTotalGb);
        cmd.Parameters.AddWithValue("@motherboard_serial", comp.MotherboardSerial ?? string.Empty);
        cmd.Parameters.AddWithValue("@pos_x", comp.PositionX);
        cmd.Parameters.AddWithValue("@pos_y", comp.PositionY);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateComputerGroupAsync(string hostname, string groupName)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "UPDATE computers SET group_name = @group WHERE hostname = @hostname;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@group", string.IsNullOrWhiteSpace(groupName) ? "Default" : groupName);
        cmd.Parameters.AddWithValue("@hostname", hostname);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateComputerRoomAsync(string hostname, string roomName)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "UPDATE computers SET room_name = @room WHERE hostname = @hostname;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@room", string.IsNullOrWhiteSpace(roomName) ? "General" : roomName);
        cmd.Parameters.AddWithValue("@hostname", hostname);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateComputerDisplayNameAsync(string hostname, string displayName)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "UPDATE computers SET display_name = @name WHERE hostname = @hostname;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@name", displayName ?? string.Empty);
        cmd.Parameters.AddWithValue("@hostname", hostname);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteComputerAsync(string hostname, string? macAddress = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            DELETE FROM computers 
            WHERE hostname = @hostname 
               OR (@mac != '' AND mac_address = @mac);
        ";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@hostname", hostname);
        cmd.Parameters.AddWithValue("@mac", macAddress ?? string.Empty);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddPendingDeletionAsync(string hostname, string? macAddress = null, string? managerId = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            INSERT INTO pending_deletions (hostname, mac_address, manager_id, created_at)
            VALUES (@hostname, @mac, @manager_id, @created_at);
        ";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@hostname", hostname);
        cmd.Parameters.AddWithValue("@mac", macAddress ?? string.Empty);
        cmd.Parameters.AddWithValue("@manager_id", managerId ?? string.Empty);
        cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> IsPendingDeletionAsync(string hostname, string? macAddress = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            SELECT 1 FROM pending_deletions 
            WHERE hostname = @hostname 
               OR (@mac != '' AND mac_address = @mac)
            LIMIT 1;
        ";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@hostname", hostname);
        cmd.Parameters.AddWithValue("@mac", macAddress ?? string.Empty);

        var result = await cmd.ExecuteScalarAsync();
        return result != null;
    }

    public async Task RemovePendingDeletionAsync(string hostname, string? macAddress = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            DELETE FROM pending_deletions 
            WHERE hostname = @hostname 
               OR (@mac != '' AND mac_address = @mac);
        ";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@hostname", hostname);
        cmd.Parameters.AddWithValue("@mac", macAddress ?? string.Empty);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<LabRoom>> GetAllRoomsAsync()
    {
        var list = new List<LabRoom>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "SELECT name, capacity, description FROM lab_rooms ORDER BY name ASC;";
        using var cmd = new SqliteCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(new LabRoom
            {
                Name = reader.GetString(0),
                Capacity = reader.IsDBNull(1) ? 30 : reader.GetInt32(1),
                Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
            });
        }

        return list;
    }

    public async Task UpsertRoomAsync(LabRoom room)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            INSERT INTO lab_rooms (name, capacity, description)
            VALUES (@name, @capacity, @description)
            ON CONFLICT(name) DO UPDATE SET
                capacity = excluded.capacity,
                description = excluded.description;
        ";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@name", room.Name);
        cmd.Parameters.AddWithValue("@capacity", room.Capacity);
        cmd.Parameters.AddWithValue("@description", room.Description ?? string.Empty);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteRoomAsync(string roomName)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "DELETE FROM lab_rooms WHERE name = @name;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@name", roomName);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetGroupsAsync()
    {
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Default" };
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "SELECT DISTINCT group_name FROM computers WHERE group_name IS NOT NULL AND group_name != '';";
        using var cmd = new SqliteCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            string g = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(g)) groups.Add(g);
        }

        return groups.OrderBy(g => g).ToList();
    }

    public async Task<List<ComputerInfo>> GetAllComputersAsync()
    {
        var list = new List<ComputerInfo>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "SELECT hostname, ip_address, mac_address, os_version, last_seen, notes, group_name, pos_x, pos_y, room_name, display_name, cpu_model, gpu_model, disk_total_gb, motherboard_serial FROM computers ORDER BY hostname ASC;";
        using var cmd = new SqliteCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var comp = new ComputerInfo
            {
                Hostname = reader.GetString(0),
                IpAddress = reader.GetString(1),
                MacAddress = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                OsVersion = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                LastSeen = DateTime.TryParse(reader.GetString(4), out var dt) ? dt : DateTime.UtcNow,
                Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                GroupName = reader.IsDBNull(6) || string.IsNullOrWhiteSpace(reader.GetString(6)) ? "Default" : reader.GetString(6),
                PositionX = reader.IsDBNull(7) ? 0 : reader.GetDouble(7),
                PositionY = reader.IsDBNull(8) ? 0 : reader.GetDouble(8),
                RoomName = reader.IsDBNull(9) || string.IsNullOrWhiteSpace(reader.GetString(9)) ? "General" : reader.GetString(9),
                DisplayName = reader.IsDBNull(10) ? null : reader.GetString(10),
                CpuModel = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                GpuModel = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                DiskTotalGb = reader.IsDBNull(13) ? 0 : reader.GetDouble(13),
                MotherboardSerial = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                IsOnline = false
            };
            list.Add(comp);
        }

        return list;
    }

    public async Task UpdateComputerPositionAsync(string hostname, double x, double y)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "UPDATE computers SET pos_x = @x, pos_y = @y WHERE hostname = @hostname;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@x", x);
        cmd.Parameters.AddWithValue("@y", y);
        cmd.Parameters.AddWithValue("@hostname", hostname);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(bool IsEnabled, TimeSpan ShutdownTime)> GetSchedulerSettingsAsync()
    {
        string enabledStr = await GetSettingAsync("DailyShutdownEnabled", "false");
        bool isEnabled = bool.TryParse(enabledStr, out bool b) && b;
        string timeStr = await GetSettingAsync("DailyShutdownTime", "17:00");
        if (!TimeSpan.TryParse(timeStr, out TimeSpan time))
        {
            time = new TimeSpan(17, 0, 0);
        }
        return (isEnabled, time);
    }

    public async Task SaveSchedulerSettingsAsync(bool isEnabled, TimeSpan shutdownTime)
    {
        await SetSettingAsync("DailyShutdownEnabled", isEnabled.ToString().ToLowerInvariant());
        await SetSettingAsync("DailyShutdownTime", shutdownTime.ToString(@"hh\:mm"));
    }

    public async Task<(bool IsEnabled, string BlacklistCsv)> GetAppGuardSettingsAsync()
    {
        string enabledStr = await GetSettingAsync("AppGuardEnabled", "false");
        bool isEnabled = bool.TryParse(enabledStr, out bool b) && b;
        string listStr = await GetSettingAsync("AppGuardBlacklist", "steam,roblox,discord,cheatengine,epicgames,valorant,bittorrent,spotify,telegram");
        return (isEnabled, listStr);
    }

    public async Task SaveAppGuardSettingsAsync(bool isEnabled, string blacklistCsv)
    {
        await SetSettingAsync("AppGuardEnabled", isEnabled.ToString().ToLowerInvariant());
        await SetSettingAsync("AppGuardBlacklist", blacklistCsv);
    }

    public async Task LogActivityAsync(string targetHost, string action, string result, bool isSuccess)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            INSERT INTO activity_logs (target_host, action, result, is_success, timestamp)
            VALUES (@host, @action, @result, @success, @time);
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@host", targetHost);
        cmd.Parameters.AddWithValue("@action", action);
        cmd.Parameters.AddWithValue("@result", result);
        cmd.Parameters.AddWithValue("@success", isSuccess ? 1 : 0);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ActivityLog>> GetActivityLogsAsync(int limit = 100)
    {
        var list = new List<ActivityLog>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "SELECT id, target_host, action, result, is_success, timestamp FROM activity_logs ORDER BY id DESC LIMIT @limit;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var log = new ActivityLog
            {
                Id = reader.GetInt64(0),
                TargetHost = reader.GetString(1),
                Action = reader.GetString(2),
                Result = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                IsSuccess = reader.GetInt32(4) == 1,
                Timestamp = DateTime.TryParse(reader.GetString(5), out var dt) ? dt : DateTime.UtcNow
            };
            list.Add(log);
        }

        return list;
    }

    public async Task<string> GetOrCreateManagerIdAsync(string? fallbackPrefix = "LAB")
    {
        string existing = await GetSettingAsync("ManagerId", string.Empty);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        string newId = $"{fallbackPrefix}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        await SetSettingAsync("ManagerId", newId);
        return newId;
    }

    public async Task<string> GetSettingAsync(string key, string defaultValue = "")
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "SELECT value FROM settings WHERE key = @key;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? defaultValue;
    }

    public async Task SetSettingAsync(string key, string value)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            INSERT INTO settings (key, value) VALUES (@key, @val)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@val", value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(string BaseUrl, string ApiKey, string Model)> GetAiRouterSettingsAsync()
    {
        string baseUrl = await GetSettingAsync("ai_router_base_url", string.Empty);
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = await GetSettingAsync("AiRouterBaseUrl", "https://api.9router.com/v1");

        string apiKey = await GetSettingAsync("ai_router_api_key", string.Empty);
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = await GetSettingAsync("AiRouterApiKey", string.Empty);

        string model = await GetSettingAsync("ai_router_model", string.Empty);
        if (string.IsNullOrWhiteSpace(model))
            model = await GetSettingAsync("AiRouterModel", "openai/gpt-4o-mini");

        return (baseUrl, apiKey, model);
    }

    public async Task SaveAiRouterSettingsAsync(string baseUrl, string apiKey, string model)
    {
        string cleanBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.9router.com/v1" : baseUrl.Trim();
        string cleanApiKey = apiKey?.Trim() ?? string.Empty;
        string cleanModel = string.IsNullOrWhiteSpace(model) ? "openai/gpt-4o-mini" : model.Trim();

        await SetSettingAsync("ai_router_base_url", cleanBaseUrl);
        await SetSettingAsync("ai_router_api_key", cleanApiKey);
        await SetSettingAsync("ai_router_model", cleanModel);

        await SetSettingAsync("AiRouterBaseUrl", cleanBaseUrl);
        await SetSettingAsync("AiRouterApiKey", cleanApiKey);
        await SetSettingAsync("AiRouterModel", cleanModel);
    }


    public async Task<string> ExportActivityLogsToCsvStringAsync(int? limit = null)
    {
        var logs = await GetActivityLogsAsync(limit ?? 10000);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("ID,TargetHost,Action,Result,IsSuccess,Timestamp");
        foreach (var log in logs)
        {
            sb.AppendLine($"{log.Id},{EscapeCsv(log.TargetHost)},{EscapeCsv(log.Action)},{EscapeCsv(log.Result)},{log.IsSuccess},{log.Timestamp:yyyy-MM-dd HH:mm:ss}");
        }
        return sb.ToString();
    }

    public async Task ExportActivityLogsToCsvFileAsync(string filePath, int? limit = null)
    {
        string csv = await ExportActivityLogsToCsvStringAsync(limit);
        await File.WriteAllTextAsync(filePath, csv, System.Text.Encoding.UTF8);
    }

    public static string ExportTelemetryToCsvString(IEnumerable<ComputerInfo> computers)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Hostname,IpAddress,MacAddress,OsVersion,Group,CpuUsage,RamUsageMb,RamTotalMb,DiskFreeGb,IsOnline,LastSeen");
        foreach (var c in computers)
        {
            string cpu = c.CpuUsage.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            string ram = c.RamUsageMb.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            string ramTotal = c.RamTotalMb.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            string disk = c.DiskFreeGb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            sb.AppendLine($"{EscapeCsv(c.Hostname)},{EscapeCsv(c.IpAddress)},{EscapeCsv(c.MacAddress)},{EscapeCsv(c.OsVersion)},{EscapeCsv(c.GroupName)},{cpu},{ram},{ramTotal},{disk},{c.IsOnline},{c.LastSeenFormatted}");
        }
        return sb.ToString();
    }

    public static string ExportHardwareInventoryCsvString(IEnumerable<ComputerInfo> computers)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Hostname,DisplayName,Room,Group,IpAddress,MacAddress,CpuModel,GpuModel,DiskTotalGb,DiskFreeGb,MotherboardSerial,OsVersion,LastSeen");
        foreach (var c in computers)
        {
            string diskTotal = c.DiskTotalGb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            string diskFree = c.DiskFreeGb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            sb.AppendLine($"{EscapeCsv(c.Hostname)},{EscapeCsv(c.DisplayName ?? string.Empty)},{EscapeCsv(c.RoomName)},{EscapeCsv(c.GroupName)},{EscapeCsv(c.IpAddress)},{EscapeCsv(c.MacAddress)},{EscapeCsv(c.CpuModel)},{EscapeCsv(c.GpuModel)},{diskTotal},{diskFree},{EscapeCsv(c.MotherboardSerial)},{EscapeCsv(c.OsVersion)},{c.LastSeenFormatted}");
        }
        return sb.ToString();
    }

    public async Task ExportHardwareInventoryCsvFileAsync(string filePath, IEnumerable<ComputerInfo>? computers = null)
    {
        var list = computers ?? await GetAllComputersAsync();
        string csv = ExportHardwareInventoryCsvString(list);
        await File.WriteAllTextAsync(filePath, csv, System.Text.Encoding.UTF8);
    }

    public static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";

        // Neutralize CSV formula injection (CWE-1236)
        if (field.StartsWith('=') || field.StartsWith('+') || field.StartsWith('-') || field.StartsWith('@'))
        {
            field = "'" + field;
        }

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return $"\"{field}\"";
    }

    public void Dispose()
    {
        // SQLite in-process clean up
    }
}
