using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>
/// The selection of multi-select (<c>components/multiSelect/multiSelect.js</c>): which cards take part, which
/// items are selected and from which lists. Cards attach themselves while they are on the page; the selection
/// toolbar and the cards redraw on every change.
/// </summary>
[AppService]
public sealed class MultiSelectState
{
    private readonly List<CardRegistration> _cards = [];
    private readonly List<(BaseItemDto Item, ItemActionContext? Context)> _selected = [];

    /// <summary>Raised when selection mode starts or stops or the selection changes.</summary>
    public event Action? Changed;

    /// <summary>True while cards show their check boxes.</summary>
    public bool IsActive { get; private set; }

    public int Count => _selected.Count;

    /// <summary>The selected items in the order they were selected.</summary>
    public IReadOnlyList<BaseItemDto> Items => _selected.Select(entry => entry.Item).ToList();

    public bool IsSelected(BaseItemDto item) => IndexOf(item) >= 0;

    /// <summary>
    /// Registers a card for as long as it is on the page: it can then be started from by its id (long press),
    /// is part of "select all", and <paramref name="changed"/> is called whenever it should redraw.
    /// </summary>
    public IDisposable Attach(Func<BaseItemDto?> item, Func<ItemActionContext?> context, Action changed)
    {
        var registration = new CardRegistration(this, item, context, changed);
        _cards.Add(registration);
        return registration;
    }

    /// <summary>Starts selection mode from the card showing the item with this id; false when there is none.</summary>
    public bool StartById(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        var key = Normalize(id);
        foreach (var card in _cards)
        {
            if (card.Item() is { } item && item.IdText() == key)
            {
                Start(item, card.Context());
                return true;
            }
        }

        return false;
    }

    /// <summary>Starts selection mode with the item selected (<c>showSelections</c>).</summary>
    public void Start(BaseItemDto item, ItemActionContext? context)
    {
        IsActive = true;
        if (IndexOf(item) < 0)
        {
            _selected.Add((item, context));
        }

        Notify();
    }

    /// <summary>Selects or deselects an item; deselecting the last one ends selection mode.</summary>
    public void Toggle(BaseItemDto item, ItemActionContext? context)
    {
        if (!IsActive)
        {
            return;
        }

        var index = IndexOf(item);
        if (index >= 0)
        {
            _selected.RemoveAt(index);
            if (_selected.Count == 0)
            {
                IsActive = false;
            }
        }
        else
        {
            _selected.Add((item, context));
        }

        Notify();
    }

    /// <summary>Selects every card on the page ("Select all").</summary>
    public void SelectAll()
    {
        if (!IsActive)
        {
            return;
        }

        foreach (var card in _cards)
        {
            if (card.Item() is { } item && IndexOf(item) < 0)
            {
                _selected.Add((item, card.Context()));
            }
        }

        Notify();
    }

    /// <summary>Ends selection mode (<c>hideSelections</c> / <c>stopMultiSelect</c>).</summary>
    public void Stop()
    {
        if (!IsActive && _selected.Count == 0)
        {
            return;
        }

        IsActive = false;
        _selected.Clear();
        Notify();
    }

    /// <summary>Reloads the lists the selected items are in (<c>dispatchNeedsRefresh</c>).</summary>
    public void RefreshLists()
    {
        foreach (var context in _selected.Select(entry => entry.Context).Where(context => context is not null).Distinct().ToList())
        {
            context!.NotifyRefreshNeeded?.Invoke();
        }
    }

    private static string Normalize(string id) => id.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private int IndexOf(BaseItemDto item)
    {
        var id = item.IdText();
        return _selected.FindIndex(entry => ReferenceEquals(entry.Item, item) || (id.Length > 0 && entry.Item.IdText() == id));
    }

    private void Notify()
    {
        Changed?.Invoke();
        foreach (var card in _cards.ToList())
        {
            card.Changed();
        }
    }

    private sealed class CardRegistration(MultiSelectState owner, Func<BaseItemDto?> item, Func<ItemActionContext?> context, Action changed)
        : IDisposable
    {
        public Func<BaseItemDto?> Item { get; } = item;

        public Func<ItemActionContext?> Context { get; } = context;

        public Action Changed { get; } = changed;

        public void Dispose() => owner._cards.Remove(this);
    }
}

/// <summary>Starts multi-select from the "Select" entry of the item context menu.</summary>
[AppService(typeof(IItemCommandHandler))]
public sealed class MultiSelectCommandHandler(MultiSelectState state) : IItemCommandHandler
{
    public bool CanHandle(string command, BaseItemDto item) => command == ItemCommand.MultiSelect;

    public Task<ItemCommandResult> ExecuteAsync(string command, BaseItemDto item, ItemActionContext context)
    {
        state.Start(item, context);
        return Task.FromResult(ItemCommandResult.None);
    }
}

/// <summary>
/// The commands of the selection toolbar (<c>showMenuForSelectedItems</c>). Collections, playlists and the
/// refresh dialog belong to other workstreams and are reached through <see cref="ItemCommandHandlers"/> with
/// <see cref="ItemActionContext.SelectedItems"/> set to the selection.
/// </summary>
[AppService]
public sealed class MultiSelectCommands(
    MultiSelectState state,
    JellyfinApi api,
    ItemQueries queries,
    ItemUserData userData,
    ItemCommandHandlers handlers,
    AppDialogs dialogs,
    LoadingService loading,
    Globalize globalize)
{
    public const string SelectAll = "selectall";
    public const string AddToCollection = "addtocollection";
    public const string AddToPlaylist = "playlist";
    public const string Delete = "delete";
    public const string GroupVersions = "groupvideos";
    public const string MarkPlayed = "markplayed";
    public const string MarkUnplayed = "markunplayed";
    public const string Refresh = "refresh";

    private static readonly string[] GroupableTypes = ["Movie", "Episode", "MusicVideo", "Video"];

    /// <summary>
    /// The entries of the menu for the current selection, in the original's order. As in the original there is
    /// no "Download" entry ("Disabled because there is no callback for this item").
    /// </summary>
    public async Task<IReadOnlyList<ActionSheetItem>> MenuAsync()
    {
        var items = state.Items;
        var policy = api.User?.Policy;
        var first = items.Count > 0 ? await FullItemAsync(items[0]) : null;
        var menu = new List<ActionSheetItem> { Entry(SelectAll, "SelectAll", "select_all") };
        if (policy?.IsAdministrator == true || policy?.EnableCollectionManagement == true)
        {
            menu.Add(Entry(AddToCollection, "AddToCollection", "add"));
        }

        menu.Add(Entry(AddToPlaylist, "AddToPlaylist", "playlist_add"));
        if (policy?.EnableContentDeletion == true)
        {
            menu.Add(Entry(Delete, "Delete", "delete"));
        }

        if (policy?.IsAdministrator == true && first is not null && GroupableTypes.Contains(first.TypeName()))
        {
            menu.Add(Entry(GroupVersions, "GroupVersions", "call_merge"));
        }

        menu.Add(Entry(MarkPlayed, "MarkPlayed", "check_box"));
        menu.Add(Entry(MarkUnplayed, "MarkUnplayed", "check_box_outline_blank"));
        if (first is not null && ItemHelper.CanRefreshMetadata(first, api.User))
        {
            menu.Add(Entry(Refresh, "RefreshMetadata", "refresh"));
        }

        return menu;
    }

    /// <summary>Runs a menu entry for the current selection.</summary>
    public async Task ExecuteAsync(string? command)
    {
        var items = state.Items;
        if (items.Count == 0 || string.IsNullOrEmpty(command))
        {
            return;
        }

        switch (command)
        {
            case SelectAll:
                state.SelectAll();
                break;
            case AddToCollection:
                await ForwardAsync(ItemCommand.AddToCollection, items);
                break;
            case AddToPlaylist:
                await ForwardAsync(ItemCommand.AddToPlaylist, items);
                break;
            case Refresh:
                await ForwardAsync(ItemCommand.Refresh, items);
                break;
            case Delete:
                await DeleteAsync(items);
                break;
            case GroupVersions:
                await GroupVersionsAsync(items);
                break;
            case MarkPlayed:
            case MarkUnplayed:
                foreach (var item in items)
                {
                    await userData.SetPlayedAsync(item, command == MarkPlayed);
                }

                Finish();
                break;
        }
    }

    private ActionSheetItem Entry(string id, string nameKey, string icon) =>
        new() { Id = id, Name = globalize[nameKey], Icon = icon };

    private async Task<BaseItemDto?> FullItemAsync(BaseItemDto item)
    {
        try
        {
            return await queries.GetItemAsync(item.IdText()) ?? item;
        }
        catch (HttpRequestException)
        {
            return item;
        }
    }

    private async Task ForwardAsync(string command, IReadOnlyList<BaseItemDto> items)
    {
        await handlers.ExecuteAsync(command, items[0], new ItemActionContext { SelectedItems = items });
        Finish();
    }

    // hideSelections + dispatchNeedsRefresh
    private void Finish()
    {
        state.RefreshLists();
        state.Stop();
    }

    private async Task DeleteAsync(IReadOnlyList<BaseItemDto> items)
    {
        var several = items.Count > 1;
        var confirmed = await dialogs.ConfirmAsync(
            globalize[several ? "ConfirmDeleteItems" : "ConfirmDeleteItem"],
            globalize[several ? "HeaderDeleteItems" : "HeaderDeleteItem"],
            globalize["Delete"],
            destructive: true);
        if (!confirmed)
        {
            return;
        }

        foreach (var item in items)
        {
            try
            {
                using var response = await api.SendAsync(HttpMethod.Delete, api.GetUrl("Items/" + item.IdText()));
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException)
            {
                await dialogs.AlertAsync(globalize["ErrorDeletingItem"]);
                break;
            }
        }

        Finish();
    }

    private async Task GroupVersionsAsync(IReadOnlyList<BaseItemDto> items)
    {
        if (items.Count < 2)
        {
            await dialogs.AlertAsync(globalize["PleaseSelectTwoItems"]);
            return;
        }

        loading.Show();
        try
        {
            var url = api.GetUrl("Videos/MergeVersions", ("Ids", string.Join(',', items.Select(item => item.IdText()))));
            using var response = await api.SendAsync(HttpMethod.Post, url);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException exception)
        {
            AppLog.Warn("Grouping versions failed: " + exception.Message);
        }
        finally
        {
            loading.Hide();
        }

        Finish();
    }
}
