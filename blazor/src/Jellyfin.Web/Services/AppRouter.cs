using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;
using Jellyfin.Web.Routing;

namespace Jellyfin.Web.Services;

/// <summary>Options for <see cref="AppRouter.GetRouteUrl(BaseItemDto, RouteOptions?)"/>.</summary>
public sealed record RouteOptions
{
    /// <summary>The collection type the link is shown in, e.g. "tvshows", "livetv", "folders".</summary>
    public string? Context { get; init; }
    public string? ParentId { get; init; }

    /// <summary>"latest" selects a library's latest tab.</summary>
    public string? Section { get; init; }
    public string? ServerId { get; init; }
}

/// <summary>Options for <see cref="AppRouter.GetListUrl"/>.</summary>
public sealed record ListRouteOptions
{
    public string? ItemTypes { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsAiring { get; init; }
    public bool IsMovie { get; init; }
    public bool IsSeries { get; init; }
    public bool IsSports { get; init; }
    public bool IsKids { get; init; }
    public bool IsNews { get; init; }
    public string? ParentId { get; init; }
}

/// <summary>
/// The app's navigation API: the current location (from the URL fragment), programmatic
/// navigation and the item → URL mapping. Port of components/router/appRouter.js.
/// Links in markup are plain <c>&lt;a href="#/path"&gt;</c>; everything else goes through here.
/// </summary>
public sealed class AppRouter
{
    /// <summary>Paths that render without a signed-in user.</summary>
    public static readonly IReadOnlySet<string> PublicPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/addserver", "/selectserver", "/login", "/forgotpassword", "/forgotpasswordpin"
    };

    private static readonly HashSet<BaseItemDto_Type> DetailTypes =
    [
        BaseItemDto_Type.Playlist, BaseItemDto_Type.TvChannel, BaseItemDto_Type.Program, BaseItemDto_Type.BoxSet,
        BaseItemDto_Type.MusicAlbum, BaseItemDto_Type.MusicGenre, BaseItemDto_Type.Person, BaseItemDto_Type.Recording,
        BaseItemDto_Type.MusicArtist
    ];

    private readonly IBrowser _browser;
    private readonly JellyfinApi _api;
    private bool _started;

    public AppRouter(IBrowser browser, JellyfinApi api)
    {
        _browser = browser;
        _api = api;
        Location = AppLocation.Parse(browser.Hash);
    }

    /// <summary>Raised after the location changed (user navigation, back/forward or <see cref="Navigate"/>).</summary>
    public event Action<AppLocation>? LocationChanged;

    public AppLocation Location { get; private set; }

    /// <summary>True when the current path needs no signed-in user.</summary>
    public bool IsOnPublicPath => PublicPaths.Contains(Location.Path);

    /// <summary>Starts following the browser's location. Called once at startup.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Location = AppLocation.Parse(_browser.Hash);
        _browser.HashChanged += hash =>
        {
            var location = AppLocation.Parse(hash);
            if (!location.Equals(Location))
            {
                Location = location;
                LocationChanged?.Invoke(location);
            }
        };
    }

    /// <summary>
    /// Navigates to an app URL. Accepts "home", "/home", "#/home" and URLs with a query.
    /// With <paramref name="replace"/> the current history entry is replaced.
    /// </summary>
    public void Navigate(string url, bool replace = false)
    {
        var target = AppLocation.Parse(url);
        _browser.Navigate(target.Url, replace);
    }

    public void Back() => _browser.Back();

    /// <summary>False on pages that are roots of the navigation (home, login, select server).</summary>
    public bool CanGoBack => Location.Path is not ("/home" or "/login" or "/selectserver" or "/");

    public void GoHome() => Navigate("/home");

    public void ShowLogin(string? serverId = null) => Navigate("/login?serverid=" + (serverId ?? _api.ServerId));

    public void ShowSelectServer() => Navigate("/selectserver");

    public void ShowSettings() => Navigate("/mypreferencesmenu");

    public void ShowNowPlaying() => Navigate("/queue");

    public void ShowVideoOsd() => Navigate("/video");

    public void ShowGuide() => Navigate("/livetv?tab=1");

    public void ShowSearch(string? query = null) =>
        Navigate(string.IsNullOrEmpty(query) ? "/search" : "/search?query=" + Uri.EscapeDataString(query));

    /// <summary>Opens the page for an item.</summary>
    public void ShowItem(BaseItemDto item, RouteOptions? options = null) => Navigate(GetRouteUrl(item, options));

    /// <summary>Opens the details page of an item by id.</summary>
    public void ShowItem(string itemId, string? serverId = null) =>
        Navigate($"/details?id={itemId}&serverId={serverId ?? _api.ServerId}");

    /// <summary>The href ("#/…") of the page that shows an item.</summary>
    public string GetRouteUrl(BaseItemDto item, RouteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        options ??= new RouteOptions();

        var context = options.Context;
        var id = Id(item.Id);
        var type = item.Type;
        var serverId = item.ServerId ?? options.ServerId ?? _api.ServerId;
        var collectionType = item.CollectionType;

        if (collectionType == BaseItemDto_CollectionType.Livetv)
        {
            return "#/livetv?collectionType=livetv";
        }

        if (type == BaseItemDto_Type.Genre)
        {
            var url = $"#/list?genreId={id}&serverId={serverId}";
            if (context == "livetv")
            {
                url += "&type=Programs";
            }

            return url + ParentSuffix(options);
        }

        if (type == BaseItemDto_Type.MusicGenre)
        {
            return $"#/list?musicGenreId={id}&serverId={serverId}" + ParentSuffix(options);
        }

        if (type == BaseItemDto_Type.Studio)
        {
            return $"#/list?studioId={id}&serverId={serverId}" + ParentSuffix(options);
        }

        if (context != "folders")
        {
            var latest = options.Section == "latest";
            switch (collectionType)
            {
                case BaseItemDto_CollectionType.Books:
                    return $"#/books?topParentId={id}&collectionType=books" + (latest ? "&tab=3" : string.Empty);
                case BaseItemDto_CollectionType.Movies:
                    return $"#/movies?topParentId={id}&collectionType=movies" + (latest ? "&tab=1" : string.Empty);
                case BaseItemDto_CollectionType.Tvshows:
                    return $"#/tv?topParentId={id}&collectionType=tvshows" + (latest ? "&tab=1" : string.Empty);
                case BaseItemDto_CollectionType.Music:
                    return $"#/music?topParentId={id}&collectionType=music" + (latest ? "&tab=1" : string.Empty);
                case BaseItemDto_CollectionType.Homevideos:
                    return $"#/homevideos?topParentId={id}";
                case BaseItemDto_CollectionType.Musicvideos:
                    return $"#/musicvideos?topParentId={id}&collectionType=musicvideos" + (latest ? "&tab=1" : string.Empty);
                case BaseItemDto_CollectionType.Boxsets:
                    return $"#/boxsets?topParentId={id}&collectionType=boxsets";
                case BaseItemDto_CollectionType.Playlists:
                    return $"#/playlists?topParentId={id}&collectionType=playlists";
                case null when type == BaseItemDto_Type.CollectionFolder:
                    return $"#/mixed?topParentId={id}&collectionType=mixed" + (latest ? "&tab=1" : string.Empty);
            }
        }

        if (type is { } known && DetailTypes.Contains(known))
        {
            return $"#/details?id={id}&serverId={serverId}";
        }

        if (type is BaseItemDto_Type.Series or BaseItemDto_Type.Season or BaseItemDto_Type.Episode)
        {
            var contextSuffix = string.IsNullOrEmpty(context) ? string.Empty : "&context=" + context;
            return $"#/details?id={id}{contextSuffix}&serverId={serverId}";
        }

        if (item.IsFolder == true)
        {
            return string.IsNullOrEmpty(id) ? "#" : $"#/list?parentId={id}&serverId={serverId}";
        }

        return $"#/details?id={id}&serverId={serverId}";
    }

    /// <summary>The href of a filtered list page ("list" in the original router).</summary>
    public string GetListUrl(ListRouteOptions options)
    {
        var url = $"#/list?serverId={_api.ServerId}&type={options.ItemTypes}";
        if (options.IsFavorite)
        {
            url += "&IsFavorite=true";
        }

        if (options.IsAiring)
        {
            url += "&IsAiring=true";
        }

        if (options.IsMovie)
        {
            url += "&IsMovie=true";
        }

        if (options.IsSeries)
        {
            url += "&IsSeries=true&IsMovie=false&IsNews=false";
        }

        if (options.IsSports)
        {
            url += "&IsSports=true";
        }

        if (options.IsKids)
        {
            url += "&IsKids=true";
        }

        if (options.IsNews)
        {
            url += "&IsNews=true";
        }

        if (!string.IsNullOrEmpty(options.ParentId))
        {
            url += "&parentId=" + options.ParentId;
        }

        return url;
    }

    /// <summary>The href of a Live TV section: programs, guide, movies, shows, sports, kids, news, onnow, channels, dvrschedule, seriesrecording.</summary>
    public string GetLiveTvUrl(string? section = null)
    {
        var serverId = _api.ServerId;
        return section switch
        {
            "programs" => $"#/livetv?tab=0&serverId={serverId}",
            "guide" => $"#/livetv?tab=1&serverId={serverId}",
            "movies" => $"#/list?type=Programs&IsMovie=true&serverId={serverId}",
            "shows" => $"#/list?type=Programs&IsSeries=true&IsMovie=false&IsNews=false&serverId={serverId}",
            "sports" => $"#/list?type=Programs&IsSports=true&serverId={serverId}",
            "kids" => $"#/list?type=Programs&IsKids=true&serverId={serverId}",
            "news" => $"#/list?type=Programs&IsNews=true&serverId={serverId}",
            "onnow" => $"#/list?type=Programs&IsAiring=true&serverId={serverId}",
            "channels" => $"#/livetv?tab=2&serverId={serverId}",
            "recordedtv" => $"#/livetv?tab=3&serverId={serverId}",
            "dvrschedule" => $"#/livetv?tab=4&serverId={serverId}",
            "seriesrecording" => $"#/livetv?tab=5&serverId={serverId}",
            _ => $"#/livetv?serverId={serverId}"
        };
    }

    /// <summary>The details page of a Live TV series recording rule (not a library item in the SDK).</summary>
    public string GetSeriesTimerUrl(string seriesTimerId) => $"#/details?seriesTimerId={seriesTimerId}&serverId={_api.ServerId}";

    public string GetNextUpUrl() => $"#/list?type=nextup&serverId={_api.ServerId}";

    public string GetTagUrl(string tag, string? parentId = null) =>
        $"#/list?type=tag&tag={Uri.EscapeDataString(tag)}&serverId={_api.ServerId}" + (string.IsNullOrEmpty(parentId) ? string.Empty : "&parentId=" + parentId);

    private static string ParentSuffix(RouteOptions options) =>
        string.IsNullOrEmpty(options.ParentId) ? string.Empty : "&parentId=" + options.ParentId;

    private static string Id(Guid? id) => id?.ToString("N") ?? string.Empty;
}
