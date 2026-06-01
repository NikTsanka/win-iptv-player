namespace IptvPlayer.Models;

public sealed class AppSettings
{
    public int Volume { get; set; } = 80;
    public string AspectRatio { get; set; } = "Auto";   // "Auto" | "16:9" | "4:3"
    public string? LastPlaylistPath { get; set; }
    public string? LastPlaylistUrl { get; set; }
}
