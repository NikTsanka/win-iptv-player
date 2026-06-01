namespace IptvPlayer.Models;

public sealed class Channel
{
    public int Number { get; set; }                      // 1-based position in the playlist
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string Group { get; set; } = "Uncategorized";
    public string? LogoUrl { get; set; }
    public string? TvgId { get; set; }

    // The stream URL is the most reliable stable key for favorites matching
    // (tvg-id is frequently missing or duplicated across providers).
    public string Id => StreamUrl;

    public override string ToString() => Name;
}
