using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>The signed-in user's libraries (GET /UserViews), cached for the shell and the home page.</summary>
public sealed class UserViews(JellyfinApi api)
{
    private Task<IReadOnlyList<BaseItemDto>>? _load;
    private Guid? _loadedFor;

    /// <summary>Raised when the cached views were dropped (sign-in, sign-out, library change).</summary>
    public event Action? Changed;

    /// <summary>The views loaded so far; empty until <see cref="GetAsync"/> completes.</summary>
    public IReadOnlyList<BaseItemDto> Current { get; private set; } = [];

    public Task<IReadOnlyList<BaseItemDto>> GetAsync(bool refresh = false)
    {
        if (!api.IsLoggedIn)
        {
            return Task.FromResult<IReadOnlyList<BaseItemDto>>([]);
        }

        if (refresh || _load is null || _loadedFor != api.UserId || _load.IsFaulted)
        {
            _loadedFor = api.UserId;
            _load = LoadAsync(api.UserId);
        }

        return _load;
    }

    /// <summary>Drops the cache; listeners reload.</summary>
    public void Invalidate()
    {
        _load = null;
        Current = [];
        Changed?.Invoke();
    }

    private async Task<IReadOnlyList<BaseItemDto>> LoadAsync(Guid? userId)
    {
        var result = await api.Client.UserViews.GetAsync(request => request.QueryParameters.UserId = userId);
        IReadOnlyList<BaseItemDto> views = result?.Items ?? [];
        if (_loadedFor == userId)
        {
            Current = views;
        }

        return views;
    }
}
