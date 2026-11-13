using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Components;

namespace Jellyfin.Web.Services;

/// <summary>
/// The item dialogs and editors by name: what the rest of the client calls where the original imports
/// <c>collectionEditor</c>, <c>playlistEditor</c>, <c>metadataEditor</c>, <c>imageeditor</c>,
/// <c>subtitleeditor</c>, <c>lyricseditor</c>, <c>itemidentifier</c>, <c>refreshdialog</c>,
/// <c>itemMediaInfo</c> and <c>fileDownloader</c>. Each method completes when its dialog has closed;
/// where the original's promise resolves or rejects, the result says whether something was changed.
/// The item menu reaches the same code through <see cref="ItemDialogCommandHandler"/>.
/// </summary>
[AppService]
public sealed class ItemDialogService(
    AppDialogs dialogs,
    ItemDialogApi dialogApi,
    JellyfinApi api,
    IItemDialogsInterop interop,
    AppRouter router,
    LoadingService loading,
    ToastService toast,
    Globalize globalize)
{
    /// <summary>"Add to collection" for the items (<c>collectionEditor.show({ items })</c>). True when they were added.</summary>
    public Task<bool> AddToCollectionAsync(IReadOnlyList<string> itemIds) => CollectionEditor.ShowAsync(dialogs, itemIds);

    /// <summary>"Add to playlist" for the items (<c>playlistEditor.show({ items })</c>). True when they were added.</summary>
    public Task<bool> AddToPlaylistAsync(IReadOnlyList<string> itemIds) => PlaylistEditor.ShowAsync(dialogs, itemIds);

    /// <summary>Editing the name and visibility of a playlist (<c>playlistEditor.show({ id })</c>). True when it was saved.</summary>
    public Task<bool> EditPlaylistAsync(string playlistId) => PlaylistEditor.ShowEditAsync(dialogs, playlistId);

    /// <summary>The metadata editor (<c>metadataEditor.show(itemId, serverId)</c>); completes when it is closed.</summary>
    public Task EditMetadataAsync(string itemId) => MetadataEditor.ShowAsync(dialogs, itemId);

    /// <summary>The image editor (<c>imageEditor.show({ itemId })</c>). True when images were changed.</summary>
    public Task<bool> EditImagesAsync(string itemId) => ImageEditor.ShowAsync(dialogs, itemId);

    /// <summary>The subtitle editor (<c>subtitleEditor.show(itemId, serverId)</c>). True when subtitles were changed.</summary>
    public Task<bool> EditSubtitlesAsync(string itemId) => SubtitleEditor.ShowAsync(dialogs, itemId);

    /// <summary>The lyrics editor (<c>lyricsEditor.show(itemId, serverId)</c>). True when the lyrics were changed.</summary>
    public Task<bool> EditLyricsAsync(string itemId) => LyricsEditor.ShowAsync(dialogs, itemId);

    /// <summary>
    /// "Identify" (<c>itemIdentifier.show(itemId, serverId)</c>): an item whose metadata is locked gets a
    /// toast instead of the dialog. True when a search result was applied.
    /// </summary>
    public async Task<bool> IdentifyAsync(string itemId)
    {
        var item = await LoadItemAsync(itemId);
        if (item is null)
        {
            return false;
        }

        if (item.Flag("LockData"))
        {
            toast.Show(globalize.Translate("MessageItemMetadataLocked"));
            return false;
        }

        return await ItemIdentifier.ShowAsync(dialogs, item);
    }

    /// <summary>
    /// "Refresh metadata" (<c>new RefreshDialog({ itemIds, mode }).show()</c>); <paramref name="mode"/>
    /// "scan" preselects scanning for new and updated files. True when a refresh was queued.
    /// </summary>
    public Task<bool> RefreshAsync(IReadOnlyList<string> itemIds, string? mode = null) => RefreshDialog.ShowAsync(dialogs, itemIds, mode);

    /// <summary>"Media info" (<c>itemMediaInfo.show(itemId, serverId)</c>); completes when the dialog is closed.</summary>
    public async Task ShowMediaInfoAsync(string itemId)
    {
        if (await LoadItemAsync(itemId) is { } item)
        {
            await ItemMediaInfo.ShowAsync(dialogs, item);
        }
    }

    /// <summary>The URL the browser downloads an item's file from (the SDK's <c>getDownloadUrl</c>).</summary>
    public string DownloadUrl(string itemId) => api.GetUrl($"Items/{itemId}/Download", ("ApiKey", api.AccessToken));

    /// <summary>"Download": the browser downloads the item's file (<c>fileDownloader.download</c>).</summary>
    public async Task DownloadAsync(string itemId) => await interop.DownloadAsync([DownloadUrl(itemId)]);

    /// <summary>
    /// "Download all": the files of the children of a collection or album, of the episodes of a season or
    /// of all seasons of a series, as far as the user may download them.
    /// </summary>
    public async Task DownloadAllAsync(BaseItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            var items = item.TypeName() switch
            {
                "BoxSet" or "MusicAlbum" => (await dialogApi.GetAsync(
                    $"Users/{api.UserIdText}/Items",
                    ("ParentId", item.IdText()),
                    ("Fields", "CanDownload,Path"))).List("Items"),
                "Season" => await EpisodesAsync(item.SeriesId?.ToString("N"), item.IdText()),
                "Series" => await SeriesEpisodesAsync(item.IdText()),
                _ => []
            };

            var urls = items.Where(child => child.Flag("CanDownload"))
                .Select(child => child.Text("Id"))
                .OfType<string>()
                .Select(DownloadUrl)
                .ToList();
            if (urls.Count > 0)
            {
                await interop.DownloadAsync(urls);
            }
        }
        catch (Exception exception) when (ItemDialogApi.IsFailure(exception))
        {
            AppLog.Warn("Loading the items to download failed: " + exception.Message);
        }
    }

    /// <summary>Whether the browser can share links (<c>appHost.supports('sharing')</c>).</summary>
    public bool CanShare => interop.CanShare;

    /// <summary>"Share": the browser's share sheet with the item's name, overview and the link to its page.</summary>
    public async Task ShareAsync(BaseItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        await interop.ShareAsync(item.Name, item.Overview, ShareUrl(item));
    }

    /// <summary>The link that is shared: the item's page in the web client the server hosts.</summary>
    public string ShareUrl(BaseItemDto item) => $"{api.ServerAddress}/web/{router.GetRouteUrl(item)}";

    private async Task<JsonObject?> LoadItemAsync(string itemId)
    {
        loading.Show();
        try
        {
            return await dialogApi.GetItemAsync(itemId);
        }
        catch (Exception exception) when (ItemDialogApi.IsFailure(exception))
        {
            AppLog.Warn("Loading the item failed: " + exception.Message);
            return null;
        }
        finally
        {
            loading.Hide();
        }
    }

    private async Task<IReadOnlyList<JsonNode?>> EpisodesAsync(string? seriesId, string? seasonId) =>
        (await dialogApi.GetAsync(
            $"Shows/{seriesId}/Episodes",
            ("seasonId", seasonId),
            ("userId", api.UserIdText),
            ("Fields", "CanDownload,Path"))).List("Items");

    private async Task<IReadOnlyList<JsonNode?>> SeriesEpisodesAsync(string seriesId)
    {
        var seasons = (await dialogApi.GetAsync(
            $"Shows/{seriesId}/Seasons",
            ("userId", api.UserIdText),
            ("Fields", "ItemCounts"))).List("Items");
        var episodes = new List<JsonNode?>();
        foreach (var season in seasons)
        {
            episodes.AddRange(await EpisodesAsync(season.Text("SeriesId"), season.Text("Id")));
        }

        return episodes;
    }
}

/// <summary>
/// The commands of the item menu that open a dialog of this area, with the results
/// <c>executeCommand()</c> of itemContextMenu.js reports: "updated" where the original asks the list to
/// refresh after the dialog resolved.
/// </summary>
[AppService(typeof(IItemCommandHandler))]
public sealed class ItemDialogCommandHandler(ItemDialogService itemDialogs) : IItemCommandHandler
{
    public bool CanHandle(string command, BaseItemDto item) => command switch
    {
        ItemCommand.AddToCollection or ItemCommand.AddToPlaylist or ItemCommand.EditPlaylist
            or ItemCommand.EditImages or ItemCommand.EditSubtitles or ItemCommand.EditLyrics or ItemCommand.Identify
            or ItemCommand.MoreMediaInfo or ItemCommand.Refresh or ItemCommand.Download or ItemCommand.DownloadAll => true,

        // Timers open the recording editors of Live TV, which are not part of this area.
        ItemCommand.Edit => item.TypeName() is not ("Timer" or "SeriesTimer"),
        ItemCommand.Share => itemDialogs.CanShare,
        _ => false
    };

    public async Task<ItemCommandResult> ExecuteAsync(string command, BaseItemDto item, ItemActionContext context)
    {
        var itemId = item.IdText();
        var selectedItemIds = context.SelectedItems is { Count: > 0 } selected
            ? selected.Select(selectedItem => selectedItem.IdText()).Where(id => id.Length > 0).ToList()
            : [itemId];
        switch (command)
        {
            case ItemCommand.AddToCollection:
                return Updated(await itemDialogs.AddToCollectionAsync(selectedItemIds));
            case ItemCommand.AddToPlaylist:
                return Updated(await itemDialogs.AddToPlaylistAsync(selectedItemIds));
            case ItemCommand.EditPlaylist:
                return Updated(await itemDialogs.EditPlaylistAsync(itemId));
            case ItemCommand.Edit:
                // The metadata editor resolves however it is closed.
                await itemDialogs.EditMetadataAsync(itemId);
                return new ItemCommandResult(Updated: true);
            case ItemCommand.EditImages:
                return Updated(await itemDialogs.EditImagesAsync(itemId));
            case ItemCommand.EditSubtitles:
                return Updated(await itemDialogs.EditSubtitlesAsync(itemId));
            case ItemCommand.EditLyrics:
                return Updated(await itemDialogs.EditLyricsAsync(itemId));
            case ItemCommand.Identify:
                return Updated(await itemDialogs.IdentifyAsync(itemId));
            case ItemCommand.MoreMediaInfo:
                await itemDialogs.ShowMediaInfoAsync(itemId);
                return ItemCommandResult.None;
            case ItemCommand.Refresh:
                await itemDialogs.RefreshAsync(selectedItemIds, item.TypeName() == "CollectionFolder" ? "scan" : null);
                return ItemCommandResult.None;
            case ItemCommand.Download:
                await itemDialogs.DownloadAsync(itemId);
                return ItemCommandResult.None;
            case ItemCommand.DownloadAll:
                await itemDialogs.DownloadAllAsync(item);
                return ItemCommandResult.None;
            case ItemCommand.Share:
                await itemDialogs.ShareAsync(item);
                return ItemCommandResult.None;
            default:
                return ItemCommandResult.None;
        }
    }

    private static ItemCommandResult Updated(bool changed) => changed ? new ItemCommandResult(Updated: true) : ItemCommandResult.None;
}
