using System.Globalization;
using System.Text.Json;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>
/// Played and favourite state of items: the server calls of <c>emby-playstatebutton</c> and
/// <c>emby-ratingbutton</c>, and one event for every change, whether it comes from this client
/// or from the server's <c>UserDataChanged</c> message. Cards, lists and buttons listen to it.
/// </summary>
[AppService]
public sealed class ItemUserData : IDisposable
{
    private readonly JellyfinApi _api;
    private readonly IDisposable _subscription;

    public ItemUserData(JellyfinApi api, ServerEvents events)
    {
        _api = api;
        _subscription = events.Subscribe("UserDataChanged", OnServerMessage);
    }

    /// <summary>Raised with the new user data of an item (<see cref="UserItemDataDto.ItemId"/> says which).</summary>
    public event Action<UserItemDataDto>? Changed;

    /// <summary>
    /// Marks an item played (<c>POST /UserPlayedItems/{id}</c>) or unplayed (<c>DELETE</c>).
    /// Returns the item's new user data.
    /// </summary>
    public async Task<UserItemDataDto?> SetPlayedAsync(BaseItemDto item, bool played)
    {
        var url = played
            ? _api.GetUrl(
                $"UserPlayedItems/{item.IdText()}",
                ("userId", _api.UserIdText),
                ("DatePlayed", DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)))
            : _api.GetUrl($"UserPlayedItems/{item.IdText()}", ("userId", _api.UserIdText));
        return await SendAsync(played ? HttpMethod.Post : HttpMethod.Delete, url, item);
    }

    /// <summary>
    /// Adds an item to the favourites (<c>POST /UserFavoriteItems/{id}</c>) or removes it
    /// (<c>DELETE</c>). Returns the item's new user data.
    /// </summary>
    public async Task<UserItemDataDto?> SetFavoriteAsync(BaseItemDto item, bool favorite)
    {
        var url = _api.GetUrl($"UserFavoriteItems/{item.IdText()}", ("userId", _api.UserIdText));
        return await SendAsync(favorite ? HttpMethod.Post : HttpMethod.Delete, url, item);
    }

    /// <summary>
    /// Copies changed user data onto an item. Returns false when <paramref name="data"/> belongs
    /// to another item.
    /// </summary>
    public static bool Apply(BaseItemDto item, UserItemDataDto data)
    {
        if (item.Id is null || data.ItemId != item.Id)
        {
            return false;
        }

        item.UserData = data;
        return true;
    }

    public void Dispose() => _subscription.Dispose();

    /// <summary>Publishes a change that did not come from the server.</summary>
    internal void Publish(UserItemDataDto data) => Changed?.Invoke(data);

    private async Task<UserItemDataDto?> SendAsync(HttpMethod method, string url, BaseItemDto item)
    {
        using var response = await _api.SendAsync(method, url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var data = string.IsNullOrWhiteSpace(json) ? null : KiotaJson.Parse(json, UserItemDataDto.CreateFromDiscriminatorValue);
        if (data is not null)
        {
            data.ItemId ??= item.Id;
            Changed?.Invoke(data);
        }

        return data;
    }

    private void OnServerMessage(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("UserDataList", out var list))
        {
            return;
        }

        if (message.TryGetProperty("UserId", out var userId) && userId.ValueKind == JsonValueKind.String
            && Guid.TryParse(userId.GetString(), out var id) && _api.UserId is { } current && id != current)
        {
            return;
        }

        foreach (var data in KiotaJson.ParseList(list, UserItemDataDto.CreateFromDiscriminatorValue))
        {
            Changed?.Invoke(data);
        }
    }
}
