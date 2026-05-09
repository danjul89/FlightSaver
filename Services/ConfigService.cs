using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlightSaver.Models;

namespace FlightSaver.Services;

public static class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlightSaver");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static Config LoadOrDefault()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new Config();
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<Config>(json, JsonOptions) ?? new Config();
        }
        catch
        {
            return new Config();
        }
    }

    public static void Save(Config config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    public static string EncryptPassword(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string? DecryptPassword(string? encryptedBase64)
    {
        if (string.IsNullOrWhiteSpace(encryptedBase64)) return null;
        try
        {
            var encrypted = Convert.FromBase64String(encryptedBase64);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
