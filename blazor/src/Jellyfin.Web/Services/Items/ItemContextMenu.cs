using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

/// <summary>
/// The options of <c>itemContextMenu.show()</c>. A command is offered when the item, the user's
/// policy and these switches allow it; the defaults are the original's (<c>!== false</c> switches
/// are on, <c>=== true</c> switches are off).
/// </summary>
public sealed record ItemMenuOptions
{
    public required BaseItemDto Item { get; init; }

    /// <summary>The signed-in user; without one only the commands that need no permission appear.</summary>
    public UserDto? User { get; init; }

    public bool Play { get; init; } = true;
    public bool PlayAllFromHere { get; init; }
    public bool QueueAllFromHere { get; init; }
    public bool Queue { get; init; } = true;
    public bool Shuffle { get; init; } = true;
    public bool InstantMix { get; init; } = true;
    public bool StopPlayback { get; init; }
    public bool ClearQueue { get; init; }

    /// <summary>"Add to playlist".</summary>
    public bool Playlist { get; init; } = true;
    public bool CancelTimer { get; init; } = true;
    public bool DeleteItem { get; init; } = true;
    public bool Edit { get; init; } = true;
    public bool EditImages { get; init; } = true;
    public bool EditSubtitles { get; init; } = true;
    public bool Identify { get; init; } = true;
    public bool MoreMediaInfo { get; init; } = true;
    public bool Record { get; init; } = true;
    public bool Share { get; init; }
    public bool OpenAlbum { get; init; } = true;
    public bool OpenArtist { get; init; } = true;

    /// <summary>The playlist the item is shown in; with <see cref="CanEditPlaylist"/> enables remove / move.</summary>
    public string? PlaylistId { get; init; }
    public bool CanEditPlaylist { get; init; }

    /// <summary>The collection the item is shown in: enables "Remove from collection".</summary>
    public string? CollectionId { get; init; }

    /// <summary>True when the menu was opened from a card: offers "Select" (multi-select).</summary>
    public bool FromCard { get; init; }

    /// <summary>The list the item belongs to, passed on to command handlers.</summary>
    public ItemActionContext? Context { get; init; }
}

/// <summary>The command chosen in the menu and what it did (<c>{ command, updated, deleted, itemId }</c>).</summary>
public sealed record ItemMenuResult(string Command, bool Updated = false, bool Deleted = false, string? ItemId = null);

/// <summary>
/// Port of <c>components/itemContextMenu.js</c>: the command list of an item with the original
/// order and visibility rules, shown as an action sheet, and the execution of the chosen command.
/// </summary>
[AppService]
public sealed class ItemContextMenu(
    Globalize globalize,
    IPlaybackManager playback,
    JellyfinApi api,
    ItemQueries queries,
    AppDialogs dialogs,
    AppRouter router,
    ToastService toast,
    UserSettings userSettings,
    ItemCommandHandlers handlers,
    ItemDeleter deleter,
    IJSRuntime js)
{
    /// <summary>The commands of the menu, dividers included (<c>getCommands</c>).</summary>
    public async Task<IReadOnlyList<ActionSheetItem>> GetCommandsAsync(ItemMenuOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var item = options.Item;
        var user = options.User;
        var policy = user?.Policy;
        var type = item.TypeName();
        var mediaType = item.MediaTypeName();
        var commands = new List<ActionSheetItem>();

        void Add(string id, string nameKey, string icon) =>
            commands.Add(new ActionSheetItem { Id = id, Name = globalize.Translate(nameKey), Icon = icon });

        void AddDivider()
        {
            // Unlike the original, never two dividers in a row.
            if (commands.Count > 0 && !commands[^1].Divider)
            {
                commands.Add(new ActionSheetItem { Divider = true });
            }
        }

        if (playback.CanPlay(item) && mediaType != "Photo")
        {
            if (options.Play)
            {
                Add(ItemCommand.Resume, "Play", "play_arrow");
            }

            if (options.PlayAllFromHere && type is not ("Program" or "TvChannel"))
            {
                Add(ItemCommand.PlayAllFromHere, "PlayAllFromHere", "play_arrow");
            }
        }

        // The original tests for a current player; the port's playback manager has no such notion
        // apart from "something is playing".
        if (playback.IsPlaying)
        {
            if (options.StopPlayback)
            {
                Add(ItemCommand.StopPlayback, "StopPlayback", "stop");
            }

            if (options.ClearQueue)
            {
                Add(ItemCommand.ClearQueue, "ClearQueue", "clear_all");
            }
        }

        if (playback.CanQueue(item) && options.Queue)
        {
            Add(ItemCommand.Queue, "AddToPlayQueue", "playlist_add");
            Add(ItemCommand.QueueNext, "PlayNext", "playlist_add");
        }

        if ((item.IsFolder == true || type is "MusicArtist" or "MusicGenre") && item.CollectionTypeName() != "livetv" && options.Shuffle)
        {
            Add(ItemCommand.Shuffle, "Shuffle", "shuffle");
        }

        if ((mediaType == "Audio" || type is "MusicAlbum" or "MusicArtist" or "MusicGenre") && options.InstantMix && !ItemHelper.IsLocalItem(item))
        {
            Add(ItemCommand.InstantMix, "InstantMix", "explore");
        }

        AddDivider();

        if (user is not null)
        {
            if (options.FromCard)
            {
                Add(ItemCommand.MultiSelect, "Select", "library_add_check");
            }

            if (ItemHelper.SupportsAddingToCollection(item) && (policy?.IsAdministrator == true || policy?.EnableCollectionManagement == true))
            {
                Add(ItemCommand.AddToCollection, "AddToCollection", "playlist_add");
            }

            if (ItemHelper.SupportsAddingToPlaylist(item) && options.Playlist)
            {
                Add(ItemCommand.AddToPlaylist, "AddToPlaylist", "playlist_add");
            }
        }

        var canManageLiveTv = policy?.EnableLiveTvManagement == true && options.CancelTimer;
        if (type == "Timer" && canManageLiveTv)
        {
            Add(ItemCommand.CancelTimer, "CancelRecording", "cancel");
        }

        if (type == "Recording" && item.Status == "InProgress" && canManageLiveTv)
        {
            Add(ItemCommand.CancelTimer, "CancelRecording", "cancel");
        }

        if (type == "SeriesTimer" && canManageLiveTv)
        {
            Add(ItemCommand.CancelSeriesTimer, "CancelSeries", "cancel");
        }

        // A browser can always download files (appHost.supports('filedownload')).
        if (policy?.EnableContentDownloading == true && type is "BoxSet" or "MusicAlbum" or "Season" or "Series")
        {
            Add(ItemCommand.DownloadAll, "DownloadAll", "file_download");
        }

        if (item.CanDownload == true && type != "Book")
        {
            Add(ItemCommand.Download, "Download", "file_download");
            Add(ItemCommand.CopyStream, "CopyStreamURL", "content_copy");
        }

        if (item.CanDelete == true && options.DeleteItem)
        {
            Add(ItemCommand.Delete, GetDeleteLabel(type), "delete");
        }

        AddDivider();

        if (type == "Playlist" && user is not null && await queries.CanEditPlaylistAsync(item.IdText()))
        {
            Add(ItemCommand.EditPlaylist, "Edit", "edit");
        }

        var canEdit = ItemHelper.CanEdit(user, item);
        if (canEdit && options.Edit && type != "SeriesTimer")
        {
            Add(ItemCommand.Edit, type == "Timer" ? "Edit" : "EditMetadata", "edit");
        }

        if (ItemHelper.CanEditImages(user, item) && options.EditImages)
        {
            Add(ItemCommand.EditImages, "EditImages", "image");
        }

        if (ItemHelper.CanEditSubtitles(user, item) && options.EditSubtitles)
        {
            Add(ItemCommand.EditSubtitles, "EditSubtitles", "closed_caption");
        }

        if (ItemHelper.CanEditLyrics(user, item))
        {
            Add(ItemCommand.EditLyrics, "EditLyrics", "lyrics");
        }

        if (options.Identify && ItemHelper.CanIdentify(user, item))
        {
            Add(ItemCommand.Identify, "Identify", "edit");
        }

        if (item.MediaSources is not null && options.MoreMediaInfo)
        {
            Add(ItemCommand.MoreMediaInfo, "MoreMediaInfo", "info");
        }

        if (type == "Program" && options.Record)
        {
            Add(ItemCommand.Record, string.IsNullOrEmpty(item.TimerId) ? "Record" : "ManageRecording", "fiber_manual_record");
        }

        if (ItemHelper.CanRefreshMetadata(item, user))
        {
            Add(ItemCommand.Refresh, "RefreshMetadata", "refresh");
        }

        var extras = item.Extras();
        if (!string.IsNullOrEmpty(item.PlaylistItemId) && !string.IsNullOrEmpty(options.PlaylistId) && options.CanEditPlaylist)
        {
            Add(ItemCommand.RemoveFromPlaylist, "RemoveFromPlaylist", "playlist_remove");
            if (extras?.PlaylistIndex is > 0)
            {
                Add(ItemCommand.MoveToTop, "MoveToTop", "vertical_align_top");
            }

            if (extras?.PlaylistIndex is { } index && index < (extras.PlaylistItemCount ?? 0) - 1)
            {
                Add(ItemCommand.MoveToBottom, "MoveToBottom", "vertical_align_bottom");
            }
        }

        if (!string.IsNullOrEmpty(options.CollectionId))
        {
            Add(ItemCommand.RemoveFromCollection, "RemoveFromCollection", "playlist_remove");
        }

        if (options.Share && ItemHelper.CanShare(user, item, handlers.CanHandle(ItemCommand.Share, item)))
        {
            Add(ItemCommand.Share, "Share", "share");
        }

        if (options.OpenAlbum && item.AlbumId is not null && mediaType != "Photo")
        {
            Add(ItemCommand.Album, "ViewAlbum", "album");
        }

        if (options.OpenArtist && item.AlbumArtists is { Count: > 0 })
        {
            Add(ItemCommand.Artist, "ViewAlbumArtist", "person");
        }

        if (item.HasLyrics == true)
        {
            Add(ItemCommand.Lyrics, "ViewLyrics", "lyrics");
        }

        // An action sheet must not end with a divider.
        while (commands.Count > 0 && commands[^1].Divider)
        {
            commands.RemoveAt(commands.Count - 1);
        }

        return commands;
    }

    /// <summary>
    /// Shows the menu and runs the chosen command. Null when there are no commands or the menu
    /// was dismissed.
    /// </summary>
    public async Task<ItemMenuResult?> ShowAsync(ItemMenuOptions options)
    {
        var commands = await GetCommandsAsync(options);
        if (commands.Count == 0)
        {
            return null;
        }

        // PORT-TODO(items): the original opens the menu at the button (positionTo); ActionSheetOptions has no
        // anchor, so the menu opens in the middle of the window.
        var id = await dialogs.ActionSheetAsync(new ActionSheetOptions { Items = commands });
        return string.IsNullOrEmpty(id) ? null : await ExecuteAsync(id, options);
    }

    /// <summary>Runs a command of the menu (<c>executeCommand</c>).</summary>
    public async Task<ItemMenuResult> ExecuteAsync(string command, ItemMenuOptions options)
    {
        var item = options.Item;
        var itemId = item.ActionId();
        var extras = item.Extras();

        switch (command)
        {
            case ItemCommand.Play:
                await PlayAsync(item, resume: false, PlayMethod.Play);
                break;
            case ItemCommand.Resume:
                await PlayAsync(item, resume: true, PlayMethod.Play);
                break;
            case ItemCommand.Queue:
                await PlayAsync(item, resume: false, PlayMethod.Queue);
                break;
            case ItemCommand.QueueNext:
                await PlayAsync(item, resume: false, PlayMethod.QueueNext);
                break;
            case ItemCommand.StopPlayback:
                await playback.StopAsync();
                break;
            case ItemCommand.Shuffle:
                await playback.ShuffleAsync(item);
                break;
            case ItemCommand.InstantMix:
                await playback.InstantMixAsync(item);
                break;
            case ItemCommand.PlayAllFromHere:
            case ItemCommand.QueueAllFromHere:
                // Needs the list: executed by the caller (ItemActions).
                break;
            case ItemCommand.Album:
                router.ShowItem(item.AlbumId.IdText(), item.ServerId);
                break;
            case ItemCommand.Artist:
                router.ShowItem(item.AlbumArtists![0].Id.IdText(), item.ServerId);
                break;
            case ItemCommand.Lyrics:
                router.ShowItem(item.IdText(), item.ServerId);
                break;
            case ItemCommand.CopyStream:
                await CopyStreamUrlAsync(item);
                break;
            case ItemCommand.Delete:
                return await deleter.DeleteAsync(item)
                    ? new ItemMenuResult(command, Updated: true, Deleted: true, ItemId: itemId)
                    : new ItemMenuResult(command);
            case ItemCommand.RemoveFromPlaylist:
                await SendAsync(HttpMethod.Delete, api.GetUrl($"Playlists/{options.PlaylistId}/Items", ("EntryIds", item.PlaylistItemId)));
                return new ItemMenuResult(command, Updated: true);
            case ItemCommand.MoveToTop:
                await SendAsync(HttpMethod.Post, api.GetUrl($"Playlists/{options.PlaylistId}/Items/{item.PlaylistItemId}/Move/0"));
                return new ItemMenuResult(command, Updated: true);
            case ItemCommand.MoveToBottom:
                await SendAsync(HttpMethod.Post, api.GetUrl($"Playlists/{options.PlaylistId}/Items/{item.PlaylistItemId}/Move/{(extras?.PlaylistItemCount ?? 1) - 1}"));
                return new ItemMenuResult(command, Updated: true);
            case ItemCommand.RemoveFromCollection:
                await SendAsync(HttpMethod.Delete, api.GetUrl($"Collections/{options.CollectionId}/Items", ("Ids", item.IdText())));
                return new ItemMenuResult(command, Updated: true);
            default:
                var result = await handlers.ExecuteAsync(command, item, options.Context ?? new ItemActionContext
                {
                    CollectionId = options.CollectionId,
                    PlaylistId = options.PlaylistId
                });
                return new ItemMenuResult(command, result.Updated, result.Deleted, result.Deleted ? itemId : null);
        }

        return new ItemMenuResult(command);
    }

    private enum PlayMethod
    {
        Play,
        Queue,
        QueueNext
    }

    private static string GetDeleteLabel(string? type) => type switch
    {
        "Series" => "DeleteSeries",
        "Episode" => "DeleteEpisode",
        "Playlist" or "BoxSet" => "Delete",
        _ => "DeleteMedia"
    };

    private Task PlayAsync(BaseItemDto item, bool resume, PlayMethod method)
    {
        var startPosition = resume ? item.UserData?.PlaybackPositionTicks ?? 0 : 0;
        PlayOptions playOptions;
        if (item.TypeName() == "Program")
        {
            playOptions = new PlayOptions { Ids = [item.ChannelId.IdText()], StartPositionTicks = startPosition };
        }
        else
        {
            var key = "items-" + (item.IsFolder == true ? item.IdText() : item.ParentId.IdText()) + "-Folder";
            var query = new Dictionary<string, string>(StringComparer.Ordinal);
            if (userSettings.Get(key + "-sortby") is { Length: > 0 } sortBy)
            {
                query["SortBy"] = sortBy;
            }

            query["SortOrder"] = userSettings.Get(key + "-sortorder") == "Descending" ? "Descending" : "Ascending";
            playOptions = new PlayOptions { Items = [item], StartPositionTicks = startPosition, QueryOptions = query };
        }

        return method switch
        {
            PlayMethod.Queue => playback.QueueAsync(playOptions),
            PlayMethod.QueueNext => playback.QueueNextAsync(playOptions),
            _ => playback.PlayAsync(playOptions)
        };
    }

    private async Task CopyStreamUrlAsync(BaseItemDto item)
    {
        // The original copies the SDK's download URL, which carries the access token as "ApiKey".
        var url = api.GetUrl($"Items/{item.IdText()}/Download", ("ApiKey", api.AccessToken));
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", url);
            toast.Show(globalize.Translate("CopyStreamURLSuccess"));
        }
        catch (JSException exception)
        {
            AppLog.Warn("Copying the stream URL failed: " + exception.Message);
            await dialogs.PromptAsync(globalize.Translate("CopyStreamURL"), value: url);
        }
    }

    private async Task SendAsync(HttpMethod method, string url)
    {
        using var response = await api.SendAsync(method, url);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Deletes items after the original's confirmation dialog (<c>scripts/deleteHelper.js</c>).</summary>
[AppService]
public sealed class ItemDeleter(AppDialogs dialogs, Globalize globalize, JellyfinApi api, AppRouter router)
{
    /// <summary>
    /// Asks, then deletes the item from the library and the disk (<c>DELETE /Items/{id}</c>).
    /// With <paramref name="navigate"/> the app then leaves the item's page for its parent or home.
    /// Returns false when the user cancelled or the server refused (an alert was shown).
    /// </summary>
    public async Task<bool> DeleteAsync(BaseItemDto item, bool navigate = false)
    {
        var (title, text, confirmText) = item.TypeName() switch
        {
            "Series" => (
                globalize.Translate("HeaderDeleteSeries"),
                globalize.Translate("ConfirmDeleteSeries", item.RecursiveItemCount),
                globalize.Translate("DeleteEntireSeries", item.RecursiveItemCount)),
            "BoxSet" => (globalize.Translate("HeaderDeleteCollection"), globalize.Translate("ConfirmDeleteCollection"), globalize.Translate("Delete")),
            "Playlist" => (globalize.Translate("HeaderDeletePlaylist"), globalize.Translate("ConfirmDeletePlaylist"), globalize.Translate("Delete")),
            _ => (globalize.Translate("HeaderDeleteItem"), globalize.Translate("ConfirmDeleteItemByName", item.Name), globalize.Translate("Delete"))
        };

        if (!await dialogs.ConfirmAsync(text, title, confirmText, destructive: true))
        {
            return false;
        }

        var deleted = false;
        try
        {
            using var response = await api.SendAsync(HttpMethod.Delete, api.GetUrl($"Items/{item.IdText()}"));
            deleted = response.IsSuccessStatusCode;
        }
        catch (HttpRequestException exception)
        {
            AppLog.Warn("Deleting an item failed: " + exception.Message);
        }

        if (!deleted)
        {
            await dialogs.AlertAsync(globalize.Translate("ErrorDeletingItem"));
            return false;
        }

        if (navigate)
        {
            var parentId = item.SeasonId ?? item.SeriesId ?? item.ParentId;
            if (parentId is not null)
            {
                router.ShowItem(parentId.IdText(), item.ServerId);
            }
            else
            {
                router.GoHome();
            }
        }

        return true;
    }
}
