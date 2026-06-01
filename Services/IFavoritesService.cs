using System.Collections.Generic;
using System.Threading.Tasks;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

public interface IFavoritesService
{
    IReadOnlyCollection<string> FavoriteIds { get; }
    bool IsFavorite(Channel channel);
    Task ToggleAsync(Channel channel);
    Task LoadAsync();
    Task SaveAsync();
}
