using System;
using System.IO;
using System.Text.Json;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

/// <summary>Synchronous JSON settings store (small payload; safe to read/write on shutdown).</summary>
public sealed class SettingsService
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IptvPlayer");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new AppSettings();
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch
        {
            // Best-effort: never let a settings write failure crash shutdown.
        }
    }
}
