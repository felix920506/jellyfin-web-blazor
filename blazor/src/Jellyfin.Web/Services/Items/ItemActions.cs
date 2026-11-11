using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

/// <summary>
/// The list an item element belongs to: what the original reads from the surrounding
/// <c>.itemsContainer</c> and the card's data attributes. <c>ItemsContainer</c> cascades one;
/// an element outside a container uses an empty context.
/// </summary>
public sealed class ItemActionContext
{
    /// <summary>The folder the list shows (<c>data-parentid</c>); used for the play order and passed to the details link.</summary>
    public string? ParentId { get; init; }

    /// <summary>Where the list is shown, e.g. "home" or a collection type (<c>data-context</c>).</summary>
    public string? Context { get; init; }

    /// <summary>Set when the list shows a collection: enables "Remove from collection".</summary>
    public string? CollectionId { get; init; }

    /// <summary>Set when the list shows a playlist: enables the playlist commands.</summary>
    public string? PlaylistId { get; init; }

    /// <summary>The items currently shown, in order ("play all from here").</summary>
    public Func<IReadOnlyList<BaseItemDto>>? Items { get; init; }

    /// <summary>Loads the list again from the server, when the container can (<c>fetchData</c>).</summary>
    public Func<Task<IReadOnlyList<BaseItemDto>>>? FetchItems { get; init; }

    /// <summary>Asks the list to reload after a command changed or deleted an item.</summary>
    public Action? NotifyRefreshNeeded { get; init; }

    /// <summary>Receives <see cref="ItemAction.Custom"/> clicks: the custom action name and the item.</summary>
    public Func<string, BaseItemDto, Task>? OnCustomAction { get; init; }

    /// <summary>Changes the context-menu options of the list before the menu opens.</summary>
    public Func<ItemMenuOptions, ItemMenuOptions>? ConfigureMenu { get; init; }
}

/// <summary>
/// Port of <c>components/shortcuts.js</c>: executes the action of a click on a card, list row or
/// one of their buttons (open, play, queue, menu…). Components call <see cref="ExecuteAsync"/>
/// from the click handler of every <c>.itemAction</c> element.
/// </summary>
[AppService]
public sealed class ItemActions(
    AppRouter router,
    IPlaybackManager playback,
    UserSettings userSettings,
    ToastService toast,
    Globalize globalize,
    ItemQueries queries,
    ItemContextMenu contextMenu,
    ItemCommandHandlers handlers,
    JellyfinApi api)
{
    /// <summary>
    /// The attributes that identify an item on its element (<c>getShortcutAttributesHtml</c>):
    /// data-id, data-serverid, data-type, data-isfolder and, when set, data-mediatype,
    /// data-collectiontype and data-channelid.
    /// </summary>
    public static IEnumerable<(string Name, string Value)> GetShortcutAttributes(BaseItemDto item)
    {
        yield return ("data-id", item.ActionId());
        yield return ("data-serverid", item.ServerId ?? string.Empty);
        yield return ("data-type", item.TypeName() ?? string.Empty);
        if (item.MediaTypeName() is { } mediaType)
        {
            yield return ("data-mediatype", mediaType);
        }

        if (item.ChannelId is not null)
        {
            yield return ("data-channelid", item.ChannelId.IdText());
        }

        yield return ("data-isfolder", item.IsFolder == true ? "true" : "false");
        if (item.CollectionTypeName() is { } collectionType)
        {
            yield return ("data-collectiontype", collectionType);
        }
    }

    /// <summary>The same attributes as a dictionary for <c>@attributes</c>.</summary>
    public static Dictionary<string, object> GetShortcutAttributeMap(BaseItemDto item)
    {
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in GetShortcutAttributes(item))
        {
            map[name] = value;
        }

        return map;
    }

    /// <summary>
    /// Executes an <see cref="ItemAction"/> for an item (<c>executeAction</c>).
    /// </summary>
    /// <param name="item">The item of the clicked card or row.</param>
    /// <param name="action">The element's <c>data-action</c>.</param>
    /// <param name="context">The surrounding list, if any.</param>
    /// <param name="customAction">The element's <c>data-customaction</c> for <see cref="ItemAction.Custom"/>.</param>
    /// <param name="playOptions">False when the element has <c>data-playoptions="false"</c>: the menu then omits the play commands.</param>
    public async Task ExecuteAsync(BaseItemDto item, string? action, ItemActionContext? context = null, string? customAction = null, bool playOptions = true)
    {
        ArgumentNullException.ThrowIfNull(item);
        context ??= new ItemActionContext();
        if (string.IsNullOrEmpty(action) || action == ItemAction.None)
        {
            return;
        }

        var type = item.TypeName();
        if (item.MediaTypeName() == "Photo" && action == ItemAction.Link)
        {
            action = ItemAction.Play;
        }

        var playableId = type == "Program" ? item.ChannelId.IdText() : item.ActionId();

        try
        {
            switch (action)
            {
                case ItemAction.Link:
                    router.ShowItem(item, new RouteOptions { Context = context.Context, ParentId = context.ParentId });
                    break;
                case ItemAction.ProgramDialog:
                    await RunHandlerAsync(ItemCommand.ProgramDialog, item, context);
                    break;
                case ItemAction.InstantMix:
                    await playback.InstantMixAsync(new BaseItemDto { Id = ParseId(playableId), ServerId = item.ServerId, Type = item.Type, MediaType = item.MediaType, Name = item.Name });
                    break;
                case ItemAction.Play:
                case ItemAction.Resume:
                    if (playback.CanPlay(item))
                    {
                        await playback.PlayAsync(new PlayOptions
                        {
                            Ids = [playableId],
                            StartPositionTicks = item.UserData?.PlaybackPositionTicks ?? 0,
                            QueryOptions = GetSortQuery(item.IsFolder == true ? item.ActionId() : context.ParentId, "SortName")
                        });
                    }

                    break;
                case ItemAction.Queue:
                    if (playback.IsPlaying)
                    {
                        await playback.QueueAsync(new PlayOptions { Ids = [playableId] });
                        toast.Show(globalize.Translate("MediaQueued"));
                    }
                    else
                    {
                        await playback.QueueAsync(new PlayOptions { Ids = [playableId] });
                    }

                    break;
                case ItemAction.PlayAllFromHere:
                    await PlayAllFromHereAsync(item, context, queue: false);
                    break;
                case ItemAction.QueueAllFromHere:
                    await PlayAllFromHereAsync(item, context, queue: true);
                    break;
                case ItemAction.SetPlaylistIndex:
                    await RunHandlerAsync(ItemCommand.SetPlaylistIndex, item, context);
                    break;
                case ItemAction.Record:
                    if (type == "Program" || !string.IsNullOrEmpty(item.TimerId) || !string.IsNullOrEmpty(item.SeriesTimerId))
                    {
                        await RunHandlerAsync(ItemCommand.ToggleRecording, item, context);
                    }

                    break;
                case ItemAction.Menu:
                    await ShowContextMenuAsync(item, context, playOptions);
                    break;
                case ItemAction.PlayMenu:
                    await RunHandlerAsync(ItemCommand.PlayMenu, item, context);
                    break;
                case ItemAction.Edit:
                    if (await queries.GetFullItemAsync(item) is { } itemToEdit)
                    {
                        await RunHandlerAsync(ItemCommand.Edit, itemToEdit, context);
                    }

                    break;
                case ItemAction.PlayTrailer:
                    await playback.PlayTrailersAsync(item);
                    break;
                case ItemAction.AddToPlaylist:
                    await RunHandlerAsync(ItemCommand.AddToPlaylist, item, context);
                    break;
                case ItemAction.Custom:
                    if (context.OnCustomAction is { } onCustom && !string.IsNullOrEmpty(customAction))
                    {
                        await onCustom(customAction, item);
                    }

                    break;
            }
        }
        catch (HttpRequestException exception)
        {
            AppLog.Warn($"Item action '{action}' failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Opens the context menu of an item of a list and runs the chosen command
    /// (<c>showContextMenu</c>). The list is asked to refresh when the command changed something.
    /// </summary>
    public async Task ShowContextMenuAsync(BaseItemDto item, ItemActionContext? context = null, bool playOptions = true)
    {
        context ??= new ItemActionContext();
        var fullItem = await queries.GetFullItemAsync(item);
        if (fullItem is null)
        {
            return;
        }

        var canEditPlaylist = false;
        if (!string.IsNullOrEmpty(context.PlaylistId))
        {
            var extras = fullItem.EnsureExtras();
            fullItem.PlaylistItemId ??= item.PlaylistItemId;
            var siblings = context.Items?.Invoke() ?? [];
            for (var i = 0; i < siblings.Count; i++)
            {
                if (siblings[i].PlaylistItemId == fullItem.PlaylistItemId)
                {
                    extras.PlaylistIndex = i;
                }
            }

            extras.PlaylistItemCount = siblings.Count;
            canEditPlaylist = await queries.CanEditPlaylistAsync(context.PlaylistId);
        }

        var options = new ItemMenuOptions
        {
            Item = fullItem,
            User = api.User,
            PlayAllFromHere = fullItem.TypeName() == "Season" || fullItem.IsFolder != true,
            QueueAllFromHere = fullItem.IsFolder != true,
            PlaylistId = context.PlaylistId,
            CanEditPlaylist = canEditPlaylist,
            CollectionId = context.CollectionId,
            FromCard = true,
            Context = context
        };
        if (!playOptions)
        {
            options = options with { Shuffle = false, InstantMix = false, Play = false, PlayAllFromHere = false, Queue = false, QueueAllFromHere = false };
        }

        if (context.ConfigureMenu is { } configure)
        {
            options = configure(options);
        }

        var result = await contextMenu.ShowAsync(options);
        if (result is null)
        {
            return;
        }

        if (result.Command is ItemCommand.PlayAllFromHere or ItemCommand.QueueAllFromHere)
        {
            await ExecuteAsync(item, result.Command, context);
        }
        else if (result.Updated || result.Deleted)
        {
            context.NotifyRefreshNeeded?.Invoke();
        }
    }

    /// <summary>
    /// The saved sort order of a folder as play query options (<c>getSortValuesLegacy</c>), so that
    /// "play" follows the order the user chose for that folder.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetSortQuery(string? folderId, string? defaultSortBy)
    {
        var key = "items-" + folderId + "-Folder";
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        var sortBy = userSettings.Get(key + "-sortby");
        if (string.IsNullOrEmpty(sortBy))
        {
            sortBy = defaultSortBy;
        }

        if (!string.IsNullOrEmpty(sortBy))
        {
            query["SortBy"] = sortBy;
        }

        query["SortOrder"] = userSettings.Get(key + "-sortorder") == "Descending" ? "Descending" : "Ascending";
        return query;
    }

    private async Task PlayAllFromHereAsync(BaseItemDto item, ItemActionContext context, bool queue)
    {
        var shown = context.Items?.Invoke() ?? [];
        var startIndex = IndexOf(shown, item);
        if (startIndex < 0)
        {
            return;
        }

        if (context.FetchItems is { } fetch)
        {
            // The original asks the list for fresh items; queueing takes them from this item on.
            var items = await fetch();
            if (queue)
            {
                await playback.QueueAsync(new PlayOptions { Items = items.Skip(startIndex).ToList() });
            }
            else
            {
                await playback.PlayAsync(new PlayOptions { Items = items, StartIndex = startIndex });
            }

            return;
        }

        var ids = shown.Select(entry => entry.ActionId()).Where(id => id.Length > 0).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        if (queue)
        {
            await playback.QueueAsync(new PlayOptions { Ids = ids.Skip(startIndex).ToList() });
        }
        else
        {
            await playback.PlayAsync(new PlayOptions { Ids = ids, StartIndex = startIndex });
        }
    }

    private async Task RunHandlerAsync(string command, BaseItemDto item, ItemActionContext context)
    {
        var result = await handlers.ExecuteAsync(command, item, context);
        if (result.Updated || result.Deleted)
        {
            context.NotifyRefreshNeeded?.Invoke();
        }
    }

    private static int IndexOf(IReadOnlyList<BaseItemDto> items, BaseItemDto item)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
            {
                return i;
            }
        }

        var id = item.ActionId();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].ActionId() == id && items[i].PlaylistItemId == item.PlaylistItemId)
            {
                return i;
            }
        }

        return -1;
    }

    private static Guid? ParseId(string id) => Guid.TryParse(id, out var guid) ? guid : null;
}
