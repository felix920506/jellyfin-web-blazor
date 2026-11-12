using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Session;
using Jellyfin.Web.Tests.Support;
using Microsoft.AspNetCore.Components;

namespace Jellyfin.Web.Tests.ItemDialogs;

public class CollectionEditorTests : ItemDialogsTestContext
{
    private static readonly string UserId = TestApp.UserId.ToString("N");
    private static readonly string[] Movies = [TestItems.Id(1), TestItems.Id(2)];

    private void HaveCollections(params (string Id, string Name)[] collections) =>
        Server.OnGet($"/Users/{UserId}/Items", new { Items = collections.Select(c => new { c.Id, c.Name }).ToArray() });

    [Fact]
    public async Task Adding_to_a_collection_lists_the_collections_and_posts_the_items()
    {
        SignIn();
        HaveCollections(("c1", "Classics"), ("c2", "Sci-Fi"));
        Server.OnPost("/Collections/c2/Items");

        var pending = Open(() => CollectionEditor.ShowAsync(Dialogs, Movies));
        Eventually(() => Assert.Equal(new[] { "", "c1", "c2" }, Options("#selectCollectionToAddTo")));

        Assert.Equal(T("HeaderAddToCollection"), DialogTitle);
        var load = Assert.Single(Server.Requests);
        Assert.Equal(
            $"/Users/{UserId}/Items?Recursive=true&IncludeItemTypes=BoxSet&SortBy=SortName&EnableTotalRecordCount=false",
            load.Uri.PathAndQuery);
        Assert.False(IsHidden(".fldSelectCollection"));
        Assert.False(IsHidden(".newCollectionInfo"));
        Assert.True(Find("#txtNewCollectionName").HasAttribute("required"));
        Assert.Equal(string.Join(',', Movies), Find(".fldSelectedItemIds").Value());

        Pick("#selectCollectionToAddTo", "c2");
        Assert.True(IsHidden(".newCollectionInfo"));
        Assert.False(Find("#txtNewCollectionName").HasAttribute("required"));

        await Submit("form.newCollectionForm");

        Assert.True(await pending);
        var add = Server.Requests.Last();
        Assert.Equal(HttpMethod.Post, add.Method);
        Assert.Equal("/Collections/c2/Items", add.Path);
        Assert.Equal(string.Join(',', Movies), add.Query("Ids"));
        Assert.Null(add.Body);
        Assert.Equal(new[] { T("MessageItemsAdded") }, Toasts);
        Assert.Empty(Dialogs.Dialogs);
        Assert.False(IsLoading);
    }

    [Fact]
    public async Task A_new_collection_is_created_with_the_items_and_opened()
    {
        SignIn();
        HaveCollections();
        Server.OnPost("/Collections", new { Id = "created" });
        var invalidated = 0;
        Get<UserViews>().Changed += () => invalidated++;

        var pending = Open(() => CollectionEditor.ShowAsync(Dialogs, Movies));
        Eventually(() => Assert.False(IsHidden(".newCollectionInfo")));
        Enter("#txtNewCollectionName", "Star Wars Collection");
        Check("#chkEnableInternetMetadata", true);
        await Submit("form.newCollectionForm");

        Assert.True(await pending);
        var create = Server.Requests.Last();
        Assert.Equal(HttpMethod.Post, create.Method);
        Assert.Equal("/Collections", create.Path);
        Assert.Equal("Star Wars Collection", create.Query("Name"));
        Assert.Equal("false", create.Query("IsLocked"));
        Assert.Equal(string.Join(',', Movies), create.Query("Ids"));
        Assert.Equal(1, invalidated);
        Assert.Equal($"/details?id=created&serverId={TestApp.ServerId}", App.Browser.Hash);
        Assert.Empty(Toasts);
    }

    [Fact]
    public async Task Without_items_only_a_new_collection_can_be_made_and_it_is_locked_by_default()
    {
        SignIn();
        Server.OnPost("/Collections", new { Id = "created" });

        var pending = Open(() => CollectionEditor.ShowAsync(Dialogs, []));
        Eventually(() => Assert.Equal(T("NewCollection"), DialogTitle));

        Assert.Empty(Server.Requests);
        Assert.True(IsHidden(".fldSelectCollection"));
        Assert.False(IsHidden(".newCollectionInfo"));

        Enter("#txtNewCollectionName", "Empty");
        await Submit("form.newCollectionForm");

        Assert.True(await pending);
        var create = Assert.Single(Server.Requests);
        Assert.Equal("Empty", create.Query("Name"));
        Assert.Equal("true", create.Query("IsLocked"));
        Assert.Equal(string.Empty, create.Query("Ids"));
    }

    [Fact]
    public async Task A_failed_save_leaves_the_dialog_open()
    {
        SignIn();
        HaveCollections(("c1", "Classics"));
        Fail(HttpMethod.Post, "/Collections/c1/Items");

        var pending = Open(() => CollectionEditor.ShowAsync(Dialogs, Movies));
        Eventually(() => Assert.Equal(2, Options("#selectCollectionToAddTo").Length));
        Pick("#selectCollectionToAddTo", "c1");
        await Submit("form.newCollectionForm");

        Assert.False(pending.IsCompleted);
        Assert.Single(Dialogs.Dialogs);
        Assert.False(IsLoading);
        Assert.Empty(Toasts);

        await Click(".btnCancel");
        Assert.False(await pending);
    }

    [Fact]
    public async Task Collections_that_cannot_be_loaded_still_allow_a_new_one()
    {
        SignIn();
        Fail(HttpMethod.Get, $"/Users/{UserId}/Items");

        var pending = Open(() => CollectionEditor.ShowAsync(Dialogs, Movies));
        Eventually(() => Assert.False(IsHidden(".newCollectionInfo")));

        Assert.Equal(new[] { "" }, Options("#selectCollectionToAddTo"));
        Assert.False(IsLoading);
        await Click(".btnCancel");
        Assert.False(await pending);
    }
}

public class PlaylistEditorTests : ItemDialogsTestContext
{
    private const string LastPlaylistSetting = "playlisteditor-lastplaylistid";
    private static readonly string UserId = TestApp.UserId.ToString("N");
    private static readonly string[] Songs = [TestItems.Id(1), TestItems.Id(2)];

    /// <summary>Playlists the user sees; only the ones with <c>CanEdit</c> may be added to.</summary>
    private void HavePlaylists(params (string Id, string Name, bool? CanEdit)[] playlists)
    {
        Server.OnGet("/Items", new { Items = playlists.Select(p => new { p.Id, p.Name }).ToArray() });
        foreach (var playlist in playlists)
        {
            if (playlist.CanEdit is { } canEdit)
            {
                Server.OnGet($"/Playlists/{playlist.Id}/Users/{UserId}", new { UserId, CanEdit = canEdit });
            }
        }
    }

    [Fact]
    public async Task Adding_to_a_playlist_offers_the_editable_playlists_and_posts_the_items()
    {
        SignIn();
        HavePlaylists(("p1", "Road trip", true), ("p2", "Shared with me", false), ("p3", "Not mine", null));
        Server.OnPost("/Playlists/p1/Items");

        var pending = Open(() => PlaylistEditor.ShowAsync(Dialogs, Songs));
        Eventually(() => Assert.Equal(new[] { "", "p1" }, Options("#selectPlaylistToAddTo")));

        Assert.Equal(T("HeaderAddToPlaylist"), DialogTitle);
        Assert.Equal(
            $"/Items?userId={UserId}&includeItemTypes=Playlist&sortBy=SortName&recursive=true&enableUserData=false",
            Server.Requests[0].Uri.PathAndQuery);
        Assert.Equal(
            new[] { $"/Playlists/p1/Users/{UserId}", $"/Playlists/p2/Users/{UserId}", $"/Playlists/p3/Users/{UserId}" },
            Server.Requests.Skip(1).Select(request => request.Path).Order());
        Assert.False(IsHidden(".fldSelectPlaylist"));
        Assert.False(IsHidden(".newPlaylistInfo"));
        Assert.Equal(T("Add"), Find("button.btnSubmit").TextContent.Trim());

        Pick("#selectPlaylistToAddTo", "p1");
        Assert.True(IsHidden(".newPlaylistInfo"));
        await Submit("form");

        Assert.True(await pending);
        var add = Server.Requests.Last();
        Assert.Equal(HttpMethod.Post, add.Method);
        Assert.Equal($"/Playlists/p1/Items?ids={Songs[0]}&ids={Songs[1]}&userId={UserId}", add.Uri.PathAndQuery);
        Assert.Equal("p1", Get<UserSettings>().Get(LastPlaylistSetting));
        Assert.Empty(Toasts);
        Assert.False(IsLoading);
    }

    [Theory]
    [InlineData("p1", "p1")]
    [InlineData("gone", "")]
    [InlineData("new", "")]
    [InlineData("queue", "")]
    public async Task The_playlist_used_last_is_selected_when_it_still_exists(string last, string selected)
    {
        SignIn();
        Get<UserSettings>().Set(LastPlaylistSetting, last);
        HavePlaylists(("p1", "Road trip", true));

        var pending = Open(() => PlaylistEditor.ShowAsync(Dialogs, Songs));
        Eventually(() => Assert.Equal(2, Options("#selectPlaylistToAddTo").Length));

        Assert.Equal(selected, Find("#selectPlaylistToAddTo").Value() ?? string.Empty);
        Assert.Equal(selected.Length > 0, IsHidden(".newPlaylistInfo"));
        await Click(".btnCancel");
        Assert.False(await pending);
    }

    [Fact]
    public void A_default_value_wins_over_the_playlist_used_last()
    {
        SignIn();
        Get<UserSettings>().Set(LastPlaylistSetting, "p1");
        HavePlaylists(("p1", "Road trip", true), ("p2", "Workout", true));

        _ = Open(() => PlaylistEditor.ShowAsync(Dialogs, Songs, defaultValue: "p2"));
        Eventually(() => Assert.Equal("p2", Find("#selectPlaylistToAddTo").Value()));

        Dialogs.Dialogs[0].Cancel();
        _ = Open(() => PlaylistEditor.ShowAsync(Dialogs, Songs, defaultValue: "new"));
        Eventually(() => Assert.Equal(3, Options("#selectPlaylistToAddTo").Length));
        Assert.Equal(string.Empty, Find("#selectPlaylistToAddTo").Value() ?? string.Empty);
    }

    [Fact]
    public async Task The_play_queue_is_offered_while_something_plays()
    {
        SignIn();
        HavePlaylists(("p1", "Road trip", true));
        Playback.Start(TestItems.Item(TestItems.Movie(9, "Playing")));

        var pending = Open(() => PlaylistEditor.ShowAsync(Dialogs, Songs));
        Eventually(() => Assert.Equal(new[] { "queue", "", "p1" }, Options("#selectPlaylistToAddTo")));

        Pick("#selectPlaylistToAddTo", "queue");
        await Submit("form");

        Assert.True(await pending);
        Assert.Equal(new[] { "Queue" }, Playback.Calls);
        Assert.Equal(Songs, Assert.Single(Playback.PlayRequests).Ids);
        Assert.Empty(Writes);
        Assert.Equal("queue", Get<UserSettings>().Get(LastPlaylistSetting));
    }

    [Fact]
    public void The_play_queue_is_not_offered_when_nothing_plays_or_the_caller_excludes_it()
    {
        SignIn();
        HavePlaylists(("p1", "Road trip", true));

        _ = Open(() => PlaylistEditor.ShowAsync(Dialogs, Songs));
        Eventually(() => Assert.Equal(new[] { "", "p1" }, Options("#selectPlaylistToAddTo")));
        Dialogs.Dialogs[0].Cancel();

        Playback.Start(TestItems.Item(TestItems.Movie(9, "Playing")));
        _ = Open(() => PlaylistEditor.ShowAsync(Dialogs, Songs, enableAddToPlayQueue: false));
        Eventually(() => Assert.Equal(new[] { "", "p1" }, Options("#selectPlaylistToAddTo")));
    }

    [Fact]
    public async Task A_new_playlist_is_created_with_the_items_and_opened()
    {
        SignIn();
        HavePlaylists();
        Server.OnPost("/Playlists", new { Id = "created" });
        var invalidated = 0;
        Get<UserViews>().Changed += () => invalidated++;

        var pending = Open(() => PlaylistEditor.ShowAsync(Dialogs, Songs));
        Eventually(() => Assert.False(IsHidden(".newPlaylistInfo")));
        Assert.True(Find("#txtNewPlaylistName").HasAttribute("required"));
        Enter("#txtNewPlaylistName", "Evening");
        Check("#chkPlaylistPublic", true);
        await Submit("form");

        Assert.True(await pending);
        var create = Server.Requests.Last();
        Assert.Equal(HttpMethod.Post, create.Method);
        Assert.Equal("/Playlists", create.Uri.PathAndQuery);
        var body = create.JsonBody();
        Assert.Equal("Evening", body.Text("Name"));
        Assert.True(body.Flag("IsPublic"));
        Assert.Equal(Songs, body.GetProperty("Ids").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal(UserId, body.Text("UserId"));
        Assert.Equal(1, invalidated);
        Assert.Equal($"/details?id=created&serverId={TestApp.ServerId}", App.Browser.Hash);
    }

    [Fact]
    public async Task A_new_playlist_without_items_is_created_empty()
    {
        SignIn();
        Server.OnPost("/Playlists", new { Id = "created" });

        var pending = Open(() => PlaylistEditor.ShowAsync(Dialogs, []));
        Eventually(() => Assert.Equal(T("HeaderNewPlaylist"), DialogTitle));

        Assert.Empty(Server.Requests);
        Assert.True(IsHidden(".fldSelectPlaylist"));
        Enter("#txtNewPlaylistName", "Later");
        await Submit("form");

        Assert.True(await pending);
        var body = Assert.Single(Server.Requests).JsonBody();
        Assert.Equal("Later", body.Text("Name"));
        Assert.False(body.Flag("IsPublic"));
        Assert.False(body.TryGetProperty("Ids", out _));
        Assert.Equal(UserId, body.Text("UserId"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_name_is_refused_with_the_create_error(string name)
    {
        SignIn();
        HavePlaylists();

        var pending = Open(() => PlaylistEditor.ShowAsync(Dialogs, Songs));
        Eventually(() => Assert.False(IsHidden(".newPlaylistInfo")));
        Enter("#txtNewPlaylistName", name);
        await Submit("form");

        Assert.Empty(Writes);
        Assert.Equal(new[] { T("PlaylistError.CreateFailed") }, Toasts);
        Assert.False(pending.IsCompleted);
        Assert.False(IsLoading);
    }

    [Fact]
    public async Task Failures_are_reported_with_the_error_of_what_was_tried()
    {
        SignIn();
        HavePlaylists(("p1", "Road trip", true));

        var pending = Open(() => PlaylistEditor.ShowAsync(Dialogs, Songs));
        Eventually(() => Assert.Equal(2, Options("#selectPlaylistToAddTo").Length));

        // Nothing answers the POSTs: the server says 404.
        Pick("#selectPlaylistToAddTo", "p1");
        await Submit("form");
        Assert.Equal(new[] { T("PlaylistError.AddFailed") }, Toasts);

        Pick("#selectPlaylistToAddTo", "");
        Enter("#txtNewPlaylistName", "Evening");
        await Submit("form");
        Assert.Equal(new[] { T("PlaylistError.AddFailed"), T("PlaylistError.CreateFailed") }, Toasts);

        Assert.Equal(new[] { "POST /Playlists/p1/Items", "POST /Playlists" }, Writes);
        Assert.False(pending.IsCompleted);
        Assert.Single(Dialogs.Dialogs);
        Assert.False(IsLoading);
    }

    [Fact]
    public async Task Editing_a_playlist_loads_and_saves_its_name_and_visibility()
    {
        SignIn();
        Server.OnGet("/Items/pl1", new { Id = "pl1", Name = "Road trip" });
        Server.OnGet("/Playlists/pl1", new { OpenAccess = true, ItemIds = Array.Empty<string>() });
        Server.OnPost("/Playlists/pl1");

        var pending = Open(() => PlaylistEditor.ShowEditAsync(Dialogs, "pl1"));
        Eventually(() => Assert.Equal("Road trip", Find("#txtNewPlaylistName").Value()));

        Assert.Equal(T("HeaderEditPlaylist"), DialogTitle);
        Assert.Equal(new[] { "/Items/pl1", "/Playlists/pl1" }, Server.Requests.Select(request => request.Uri.PathAndQuery).Order());
        Assert.True(Find("#chkPlaylistPublic").IsChecked());
        Assert.True(IsHidden(".fldSelectPlaylist"));
        Assert.False(IsHidden(".newPlaylistInfo"));
        Assert.Equal(T("Save"), Find("button.btnSubmit").TextContent.Trim());

        Enter("#txtNewPlaylistName", "Road trip 2");
        Check("#chkPlaylistPublic", false);
        await Submit("form");

        Assert.True(await pending);
        var save = Server.Requests.Last();
        Assert.Equal(HttpMethod.Post, save.Method);
        Assert.Equal("/Playlists/pl1", save.Uri.PathAndQuery);
        Assert.Equal("""{"Name":"Road trip 2","IsPublic":false}""", save.Body);
        Assert.Empty(Toasts);
    }

    [Fact]
    public async Task A_failed_or_blank_edit_shows_the_update_error()
    {
        SignIn();
        Server.OnGet("/Items/pl1", new { Id = "pl1", Name = "Road trip" });
        Server.OnGet("/Playlists/pl1", new { OpenAccess = false });

        var pending = Open(() => PlaylistEditor.ShowEditAsync(Dialogs, "pl1"));
        Eventually(() => Assert.Equal("Road trip", Find("#txtNewPlaylistName").Value()));

        await Submit("form");
        Assert.Equal(new[] { "POST /Playlists/pl1" }, Writes);
        Assert.Equal(new[] { T("PlaylistError.UpdateFailed") }, Toasts);

        Enter("#txtNewPlaylistName", "");
        await Submit("form");
        Assert.Single(Writes);
        Assert.Equal(2, Toasts.Length);
        Assert.False(pending.IsCompleted);
    }
}
