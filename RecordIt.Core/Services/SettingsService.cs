using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace RecordIt.Core.Services;

public class SettingsService
{
    private readonly string _dbPath;

    public SettingsService()
    {
        var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RecordIt");
        Directory.CreateDirectory(appDir);
        _dbPath = Path.Combine(appDir, "settings.db");
        Initialize();
    }

    private void Initialize()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT)";
        cmd.ExecuteNonQuery();
    }

    public void Set(string key, string value)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public string? Get(string key)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var r = cmd.ExecuteScalar();
        return r == null ? null : r.ToString();
    }
}
