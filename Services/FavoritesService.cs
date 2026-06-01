using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

public sealed class FavoritesService : IFavoritesService
{
    private readonly string _filePath;
    private readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public FavoritesService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IptvPlayer");
        Directory.CreateDirectory(dir);                 // safe if it already exists
        _filePath = Path.Combine(dir, "favorites.json");
    }

    public IReadOnlyCollection<string> FavoriteIds => _ids;

    public bool IsFavorite(Channel channel) =>
        channel is not null && _ids.Contains(channel.Id);

    public async Task ToggleAsync(Channel channel)
    {
        if (channel is null) return;
        if (!_ids.Add(channel.Id))   // Add returns false if it was already present
            _ids.Remove(channel.Id);
        await SaveAsync();
    }

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            await using var fs = File.OpenRead(_filePath);
            var ids = await JsonSerializer.DeserializeAsync<List<string>>(fs);
            _ids.Clear();
            if (ids is not null)
                foreach (var id in ids) _ids.Add(id);
        }
        catch
        {
            // Corrupt/partial file -> degrade to empty favorites instead of crashing.
        }
    }

    public async Task SaveAsync()
    {
        // Write to a temp file then swap, so a crash mid-write can't corrupt the store.
        var tmp = _filePath + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, _ids.ToList(), JsonOpts);
        File.Copy(tmp, _filePath, overwrite: true);
        File.Delete(tmp);
    }
}
