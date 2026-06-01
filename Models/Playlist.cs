using System;
using System.Collections.Generic;
using System.Linq;

namespace IptvPlayer.Models;

public sealed class Playlist
{
    public string? Source { get; set; }                  // originating file path or URL
    public List<Channel> Channels { get; } = new();

    // Distinct, case-insensitive, alphabetically ordered group names.
    public IEnumerable<string> Groups =>
        Channels.Select(c => c.Group)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase);
}
