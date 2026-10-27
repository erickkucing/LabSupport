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

        string sql = @"
            CREATE TABLE IF NOT EXISTS computers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                hostname TEXT UNIQUE NOT NULL,
                ip_address TEXT NOT NULL,
                mac_address TEXT,
                os_version TEXT,
                last_seen TEXT NOT NULL,
                notes TEXT
            );

            CREATE TABLE IF NOT EXISTS activity_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                target_host TEXT NOT NULL,
                action TEXT NOT NULL,
                result TEXT,
                is_success INTEGER NOT NULL,
                timestamp TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );
        ";

        using var cmd = new SqliteCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpsertComputerAsync(ComputerInfo comp)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            INSERT INTO computers (hostname, ip_address, mac_address, os_version, last_seen, notes)
            VALUES (@hostname, @ip, @mac, @os, @last_seen, @notes)
            ON CONFLICT(hostname) DO UPDATE SET
                ip_address = excluded.ip_address,
                mac_address = excluded.mac_address,
                os_version = excluded.os_version,
                last_seen = excluded.last_seen;
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@hostname", comp.Hostname);
        cmd.Parameters.AddWithValue("@ip", comp.IpAddress);
        cmd.Parameters.AddWithValue("@mac", comp.MacAddress ?? string.Empty);
        cmd.Parameters.AddWithValue("@os", comp.OsVersion ?? string.Empty);
        cmd.Parameters.AddWithValue("@last_seen", comp.LastSeen.ToString("o"));
        cmd.Parameters.AddWithValue("@notes", comp.Notes ?? string.Empty);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ComputerInfo>> GetAllComputersAsync()
    {
        var list = new List<ComputerInfo>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "SELECT hostname, ip_address, mac_address, os_version, last_seen, notes FROM computers ORDER BY hostname ASC;";
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
                IsOnline = false
            };
            list.Add(comp);
        }

        return list;
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

    public void Dispose()
    {
        // SQLite in-process clean up
    }
}
