using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

/// <summary>Loads playlist text from a local file or an HTTP(S) URL, then parses it.</summary>
public sealed class PlaylistLoader
{
    // One shared, long-lived HttpClient (avoids socket exhaustion).
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<Playlist> LoadFromFileAsync(string path, CancellationToken ct = default)
    {
        // UTF-8 read (BOM auto-detected) renders Georgian / Russian / English correctly.
        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        return M3UParser.Parse(content, path);
    }

    public async Task<Playlist> LoadFromUrlAsync(string url, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync(ct);
        return M3UParser.Parse(content, url);
    }
}
