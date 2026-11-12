using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Session;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.ItemDialogs;

/// <summary>What the metadata editor tests share: the items and the editor info the server answers with.</summary>
public abstract class MetadataEditorTestContext : ItemDialogsTestContext
{
    protected const string Path = "/media/movies/Third Film (2008)/Third Film.mkv";
    protected static readonly string UserId = TestApp.UserId.ToString("N");
    protected static readonly string ItemId = TestItems.Id(1);
    protected static readonly string ParentId = TestItems.Id(9);

    protected static JsonObject Movie() => JsonNode.Parse($$"""
        {
            "Id": "{{ItemId}}", "Type": "Movie", "MediaType": "Video", "Name": "Third Film", "OriginalTitle": "Dritter Film",
            "OriginalLanguage": "de", "ForcedSortName": "third film", "Path": "{{Path}}", "Overview": "An overview.",
            "CommunityRating": 7.5, "CriticRating": 80, "ProductionYear": 2008, "CanDelete": true,
            "PremiereDate": "2008-05-17T10:20:30.0000000Z", "DateCreated": "2024-01-02T03:04:05.0000000Z",
            "OfficialRating": "PG-13", "CustomRating": "Home", "Genres": ["Drama", "Action"], "Tags": ["beta", "Alpha"],
            "Studios": [{ "Name": "Studio B", "Id": "{{TestItems.Id(22)}}" }, { "Name": "Studio A", "Id": "{{TestItems.Id(21)}}" }],
            "People": [
                { "Name": "Ann Actor", "Id": "{{TestItems.Id(11)}}", "Role": "Hero", "Type": "Actor" },
                { "Name": "Dan Director", "Id": "{{TestItems.Id(12)}}", "Type": "Director" }
            ],
            "ProviderIds": { "Tmdb": "42", "Imdb": "tt0000042", "Legacy": "x" }, "Taglines": ["One line.", "Another."],
            "LockData": false, "LockedFields": ["Genres", "Cast"], "PreferredMetadataLanguage": "en",
            "PreferredMetadataCountryCode": "US", "AspectRatio": "16:9", "Video3DFormat": "MVC", "ParentId": "{{ParentId}}"
        }
        """)!.AsObject();

    /// <summary>An item of a type with nothing but what every item has.</summary>
    protected static JsonObject Item(string type, string? mediaType = null, Action<JsonObject>? configure = null)
    {
        var item = new JsonObject { ["Id"] = ItemId, ["Type"] = type, ["Name"] = "Some " + type, ["Path"] = "/media/some" };
        if (mediaType is not null)
        {
            item["MediaType"] = mediaType;
        }

        configure?.Invoke(item);
        return item;
    }

    protected static JsonObject Info(Action<JsonObject>? configure = null)
    {
        var info = JsonNode.Parse("""
            {
                "ContentTypeOptions": [],
                "ExternalIdInfos": [
                    { "Name": "TheMovieDb", "Key": "Tmdb" },
                    { "Name": "IMDb", "Key": "Imdb" },
                    { "Name": "TheMovieDb", "Key": "TmdbCollection", "Type": "BoxSet" }
                ],
                "ParentalRatingOptions": [{ "Name": "G", "Value": 1 }, { "Name": "PG-13", "Value": 13 }],
                "Cultures": [
                    { "DisplayName": "English", "TwoLetterISOLanguageName": "en" },
                    { "DisplayName": "German", "TwoLetterISOLanguageName": "de" }
                ],
                "Countries": [
                    { "DisplayName": "United States", "TwoLetterISORegionName": "US" },
                    { "DisplayName": "Germany", "TwoLetterISORegionName": "DE" }
                ]
            }
            """)!.AsObject();
        configure?.Invoke(info);
        return info;
    }

    protected void Have(JsonObject item, JsonObject? info = null)
    {
        var id = item.Text("Id");
        Server.OnGet($"/Users/{UserId}/Items/{id}", item.ToJsonString());
        Server.OnGet($"/Items/{id}/MetadataEditor", (info ?? Info()).ToJsonString());
    }

    /// <summary>Opens the editor for an item the server knows and waits until it is filled in.</summary>
    protected Task OpenEditor(JsonObject item, JsonObject? info = null)
    {
        Have(item, info);
        Server.OnPost($"/Items/{ItemId}");
        var pending = Open(() => MetadataEditor.ShowAsync(Dialogs, ItemId));
        Eventually(() => Assert.Equal(item.Text("Name"), Val("#txtName")));
        return pending;
    }

    /// <summary>The value of an input, a text area or a select.</summary>
    protected string Val(string selector) => Find(selector).Value() ?? string.Empty;

    protected string Label(string input) => Find($"label[for={input}]").TextContent.Trim();

    protected string[] Texts(string selector) => FindAll(selector).Select(element => element.TextContent.Trim()).ToArray();

    /// <summary>Saves and hands back what was posted for the item.</summary>
    protected async Task<JsonElement> Save()
    {
        await Submit("form.editItemMetadataForm");
        return Server.Requests.Last(request => request.Method == HttpMethod.Post && request.Path == "/Items/" + ItemId).JsonBody();
    }

    protected static string Normalized(string json) => JsonNode.Parse(json)!.ToJsonString();

    protected static string?[] Strings(JsonElement array) => array.EnumerateArray().Select(value => value.GetString()).ToArray();
}

public class MetadataEditorTests : MetadataEditorTestContext
{
    // Every container the editor shows or hides by the type of the item.
    private static readonly string[] Containers =
    [
        "fldPath", "fldOriginalName", "fldOriginalLanguage", "fldDateAdded", "fldStatus", "fldArtist", "fldAlbumArtist", "seriesName",
        "fldAlbum", "fldParentIndexNumber", "fldIndexNumber", "fldCommunityRating", "fldCriticRating", "fldTagline", "overviewContainer",
        "fldPremiereDate", "fldYear", "fldPlaceOfBirth", "fldEndDate", "fldAirDays", "fldAirTime", "fldSeriesRuntime", "fldOfficialRating",
        "fldCustomRating", "fldHeight", "fldOriginalAspectRatio", "fld3dFormat", "fldDisplayOrder", "collapsibleSpecialEpisodeInfo",
        "genresCollapsible", "peopleCollapsible", "studiosCollapsible", "tagsCollapsible", "metadataSettingsCollapsible"
    ];

    // What every item that is not a person, genre, studio or channel shows.
    private const string Common =
        "fldPath fldDateAdded fldCommunityRating overviewContainer fldPremiereDate fldYear fldOfficialRating fldCustomRating "
        + "genresCollapsible peopleCollapsible studiosCollapsible tagsCollapsible metadataSettingsCollapsible ";

    private string[] Visible() =>
        Containers.Where(container => !IsHidden(container == "overviewContainer" ? ".overviewContainer" : "#" + container)).Order().ToArray();

    [Fact]
    public async Task The_editor_shows_the_item_as_the_server_has_it()
    {
        SignIn(admin: true);

        var pending = OpenEditor(Movie());

        Assert.Equal(T("Edit"), DialogTitle);
        Assert.Equal(
            new[] { $"/Users/{UserId}/Items/{ItemId}", $"/Items/{ItemId}/MetadataEditor" },
            Server.Requests.Select(request => request.Uri.PathAndQuery));
        Assert.False(IsLoading);

        Assert.True(IsHidden("#fldContentType"));
        Assert.Equal(Path, Val("#txtPath"));
        Assert.True(Find("#txtPath").HasAttribute("readonly"));
        Assert.True(Find("#txtName").HasAttribute("required"));
        Assert.Equal("Dritter Film", Val("#txtOriginalName"));
        Assert.Equal("de", Val("#selectOriginalLanguage"));
        Assert.Equal(new[] { "", "en", "de" }, Options("#selectOriginalLanguage"));
        Assert.Equal("third film", Val("#txtSortName"));
        Assert.Equal("2024-01-02", Val("#txtDateAdded"));
        Assert.Equal("date", Find("#txtDateAdded").GetAttribute("type"));
        Assert.Equal("7.5", Val("#txtCommunityRating"));
        Assert.Equal("80", Val("#txtCriticRating"));
        Assert.Equal("One line.", Val("#txtTagline"));
        Assert.Equal("An overview.", Val("#txtOverview"));
        Assert.Equal("2008-05-17", Val("#txtPremiereDate"));
        Assert.Equal("2008", Val("#txtProductionYear"));
        Assert.Equal("PG-13", Val("#selectOfficialRating"));
        Assert.Equal(new[] { "", "G", "PG-13" }, Options("#selectOfficialRating"));
        Assert.Equal("Home", Val("#selectCustomRating"));
        Assert.Equal(new[] { "", "G", "PG-13", "Home" }, Options("#selectCustomRating"));
        Assert.Equal("16:9", Val("#txtOriginalAspectRatio"));
        Assert.Equal("MVC", Val("#select3dFormat"));
        Assert.Equal(new[] { "", "HalfSideBySide", "HalfTopAndBottom", "FullSideBySide", "FullTopAndBottom", "MVC" }, Options("#select3dFormat"));
        Assert.Equal("en", Val("#selectLanguage"));
        Assert.Equal("US", Val("#selectCountry"));
        Assert.Equal(new[] { "", "US", "DE" }, Options("#selectCountry"));

        // The lists are sorted by name, whatever the case.
        Assert.Equal(new[] { "Action", "Drama" }, Texts("#listGenres .textValue"));
        Assert.Equal(new[] { "Alpha", "beta" }, Texts("#listTags .textValue"));
        Assert.Equal(new[] { "Studio A", "Studio B" }, Texts("#listStudios .textValue"));
        Assert.Equal(new[] { "Ann Actor", "Dan Director" }, Texts("#peopleList .textValue"));
        Assert.Equal(new[] { "Hero", T("Director") }, Texts("#peopleList .secondary"));

        await Click(".formDialogFooter .btnCancel");
        await pending;
        Assert.Empty(Writes);
        Assert.Empty(Toasts);
    }

    [Theory]
    [InlineData("Movie", "Video", Common + "fldOriginalName fldOriginalLanguage fldCriticRating fldTagline fldOriginalAspectRatio fld3dFormat")]
    [InlineData("Trailer", "Video", Common + "fldOriginalName fldOriginalLanguage fldCriticRating fldTagline fldOriginalAspectRatio fld3dFormat")]
    [InlineData("Series", null, Common + "fldOriginalName fldOriginalLanguage fldStatus fldEndDate fldAirDays fldAirTime fldSeriesRuntime fldDisplayOrder")]
    [InlineData("Season", null, Common + "fldOriginalName fldIndexNumber")]
    [InlineData("Episode", "Video", Common + "fldOriginalName fldOriginalLanguage fldParentIndexNumber fldIndexNumber fldOriginalAspectRatio fld3dFormat")]
    [InlineData("Audio", "Audio", Common + "fldArtist fldAlbumArtist fldAlbum fldParentIndexNumber fldIndexNumber")]
    [InlineData("MusicAlbum", null, Common + "fldArtist fldAlbumArtist")]
    [InlineData("MusicVideo", "Video", Common + "fldOriginalLanguage fldArtist fldAlbumArtist fldAlbum fldTagline fldOriginalAspectRatio fld3dFormat")]
    [InlineData("Book", null, Common + "seriesName")]
    [InlineData("BoxSet", null, Common + "fldDisplayOrder")]
    [InlineData("Folder", null, Common)]
    [InlineData("Person", null, "fldPath fldOriginalName fldDateAdded overviewContainer fldPremiereDate fldYear fldPlaceOfBirth fldEndDate tagsCollapsible metadataSettingsCollapsible")]
    [InlineData("Genre", null, "fldPath fldDateAdded overviewContainer fldPremiereDate fldYear tagsCollapsible metadataSettingsCollapsible")]
    [InlineData("MusicGenre", null, "fldPath fldDateAdded overviewContainer fldPremiereDate fldYear tagsCollapsible metadataSettingsCollapsible")]
    [InlineData("Studio", null, "fldPath fldDateAdded overviewContainer fldPremiereDate fldYear tagsCollapsible metadataSettingsCollapsible")]
    [InlineData("TvChannel", "Video", "fldPath fldOriginalLanguage fldOfficialRating fldHeight tagsCollapsible")]
    public void The_fields_depend_on_the_type_of_the_item(string type, string? mediaType, string visible)
    {
        SignIn(admin: true);

        _ = OpenEditor(Item(type, mediaType));

        Assert.Equal(visible.Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(), Visible());
    }

    [Fact]
    public void An_item_without_a_path_or_with_hidden_media_sources_shows_no_path()
    {
        SignIn(admin: true);

        _ = OpenEditor(Item("Movie", "Video", item => item.Remove("Path")));
        Assert.True(IsHidden("#fldPath"));
    }

    [Fact]
    public void An_item_whose_media_sources_are_not_shown_shows_no_path()
    {
        SignIn(admin: true);

        _ = OpenEditor(Item("Movie", "Video", item => item["EnableMediaSourceDisplay"] = false));
        Assert.True(IsHidden("#fldPath"));
    }

    [Theory]
    [InlineData("Movie", "LabelTitle", "LabelSortTitle", "LabelOriginalTitle", "LabelYear", "LabelReleaseDate", "LabelEndDate")]
    [InlineData("Series", "LabelTitle", "LabelSortTitle", "LabelOriginalTitle", "LabelYear", "LabelReleaseDate", "LabelEndDate")]
    [InlineData("Person", "LabelName", "LabelSortName", "LabelOriginalName", "LabelBirthYear", "LabelBirthDate", "LabelDeathDate")]
    public void People_have_names_and_birth_dates_where_other_items_have_titles_and_release_dates(
        string type, string name, string sortName, string originalName, string year, string premiereDate, string endDate)
    {
        SignIn(admin: true);

        _ = OpenEditor(Item(type));

        Assert.Equal(T(name), Label("txtName"));
        Assert.Equal(T(sortName), Label("txtSortName"));
        Assert.Equal(T(originalName), Label("txtOriginalName"));
        Assert.Equal(T(year), Label("txtProductionYear"));
        Assert.Equal(T(premiereDate), Label("txtPremiereDate"));
        Assert.Equal(T(endDate), Label("txtEndDate"));
    }

    [Theory]
    [InlineData("Episode", "LabelEpisodeNumber", "LabelSeasonNumber")]
    [InlineData("Season", "LabelSeasonNumber", "LabelParentNumber")]
    [InlineData("Audio", "LabelTrackNumber", "LabelDiscNumber")]
    [InlineData("Movie", "LabelNumber", "LabelParentNumber")]
    public void The_numbers_are_named_after_what_they_count(string type, string number, string parentNumber)
    {
        SignIn(admin: true);

        _ = OpenEditor(Item(type));

        Assert.Equal(T(number), Label("txtIndexNumber"));
        Assert.Equal(T(parentNumber), Label("txtParentIndexNumber"));
    }

    [Fact]
    public void An_editor_whose_item_cannot_be_loaded_shows_the_empty_form()
    {
        SignIn(admin: true);

        var pending = Open(() => MetadataEditor.ShowAsync(Dialogs, ItemId));
        Eventually(() => Assert.Equal(2, Server.Requests.Count));
        Eventually(() => Assert.False(IsLoading));

        // The labels of the original's template; the numbers have none before an item is loaded.
        Assert.Equal(T("LabelTitle"), Label("txtName"));
        Assert.Equal(T("LabelSortTitle"), Label("txtSortName"));
        Assert.Equal(T("LabelOriginalTitle"), Label("txtOriginalName"));
        Assert.Equal(T("LabelYear"), Label("txtProductionYear"));
        Assert.Equal(T("LabelReleaseDate"), Label("txtPremiereDate"));
        Assert.Equal(T("LabelEndDate"), Label("txtEndDate"));
        Assert.Equal(string.Empty, Label("txtIndexNumber"));
        Assert.Equal(string.Empty, Val("#txtName"));
        Assert.True(IsHidden(".externalIdsSection"));
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task Nothing_is_saved_for_an_item_that_could_not_be_loaded()
    {
        SignIn(admin: true);
        var pending = Open(() => MetadataEditor.ShowAsync(Dialogs, ItemId));
        Eventually(() => Assert.False(IsLoading));

        await Submit("form.editItemMetadataForm");

        Assert.Empty(Writes);
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task Saving_sends_every_field_of_the_item()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Movie());

        await Submit("form.editItemMetadataForm");
        await pending;

        Assert.Equal(new[] { "POST /Items/" + ItemId }, Writes);
        var request = Server.Requests.Last();
        Assert.Equal("/Items/" + ItemId, request.Uri.PathAndQuery);
        Assert.Equal(
            Normalized($$"""
                {
                    "Id": "{{ItemId}}", "Name": "Third Film", "OriginalTitle": "Dritter Film", "OriginalLanguage": "de",
                    "ForcedSortName": "third film", "CommunityRating": "7.5", "CriticRating": "80", "IndexNumber": null,
                    "AirsBeforeSeasonNumber": "", "AirsAfterSeasonNumber": "", "AirsBeforeEpisodeNumber": "", "ParentIndexNumber": null,
                    "DisplayOrder": "", "Album": "", "AlbumArtists": [], "ArtistItems": [], "SeriesName": "", "Overview": "An overview.",
                    "Status": "", "AirDays": [], "AirTime": "", "Genres": ["Action", "Drama"], "Tags": ["Alpha", "beta"],
                    "Studios": [{ "Name": "Studio A" }, { "Name": "Studio B" }],
                    "PremiereDate": "2008-05-17T10:20:30.000Z", "DateCreated": "2024-01-02T03:04:05.000Z", "EndDate": null,
                    "ProductionYear": "2008", "Height": "", "AspectRatio": "16:9", "Video3DFormat": "MVC", "OfficialRating": "PG-13",
                    "CustomRating": "Home",
                    "People": [
                        { "Name": "Ann Actor", "Id": "{{TestItems.Id(11)}}", "Role": "Hero", "Type": "Actor" },
                        { "Name": "Dan Director", "Id": "{{TestItems.Id(12)}}", "Type": "Director" }
                    ],
                    "LockData": false, "LockedFields": ["Genres", "Cast"],
                    "ProviderIds": { "Tmdb": "42", "Imdb": "tt0000042", "Legacy": "x", "TmdbCollection": "" },
                    "PreferredMetadataLanguage": "en", "PreferredMetadataCountryCode": "US", "Taglines": ["One line."]
                }
                """),
            Normalized(request.Body!));
        Assert.Equal(new[] { T("MessageItemSaved") }, Toasts);
        Assert.False(IsLoading);
        Assert.Empty(Dialogs.Dialogs);
    }

    [Fact]
    public async Task What_was_entered_is_saved()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Movie());

        Enter("#txtName", "Third Film Redux");
        Enter("#txtOriginalName", "");
        Pick("#selectOriginalLanguage", "en");
        Enter("#txtSortName", "redux");
        Enter("#txtCommunityRating", "8.25");
        Enter("#txtCriticRating", "");
        Enter("#txtTagline", "");
        Enter("#txtOverview", "Another overview.");
        Enter("#txtPremiereDate", "2009-01-02");
        Enter("#txtDateAdded", "");
        Enter("#txtProductionYear", "2009");
        Pick("#selectOfficialRating", "G");
        Pick("#selectCustomRating", "");
        Enter("#txtOriginalAspectRatio", "4:3");
        Pick("#select3dFormat", "");
        Pick("#selectLanguage", "de");
        Pick("#selectCountry", "DE");
        Enter("#txt1Tmdb", "43");
        Enter("#txt1TmdbCollection", "7");
        var body = await Save();
        await pending;

        Assert.Equal("Third Film Redux", body.Text("Name"));
        Assert.Equal("", body.Text("OriginalTitle"));
        Assert.Equal("en", body.Text("OriginalLanguage"));
        Assert.Equal("redux", body.Text("ForcedSortName"));
        Assert.Equal("8.25", body.Text("CommunityRating"));
        Assert.Equal("", body.Text("CriticRating"));
        Assert.Equal(0, body.GetProperty("Taglines").GetArrayLength());
        Assert.Equal("Another overview.", body.Text("Overview"));

        // A new day is sent as it was entered, an emptied date not at all.
        Assert.Equal("2009-01-02", body.Text("PremiereDate"));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("DateCreated").ValueKind);
        Assert.Equal("2009", body.Text("ProductionYear"));
        Assert.Equal("G", body.Text("OfficialRating"));
        Assert.Equal("", body.Text("CustomRating"));
        Assert.Equal("4:3", body.Text("AspectRatio"));
        Assert.Equal("", body.Text("Video3DFormat"));
        Assert.Equal("de", body.Text("PreferredMetadataLanguage"));
        Assert.Equal("DE", body.Text("PreferredMetadataCountryCode"));
        Assert.Equal(
            """{"Tmdb":"43","Imdb":"tt0000042","Legacy":"x","TmdbCollection":"7"}""",
            Normalized(body.GetProperty("ProviderIds").GetRawText()));
    }

    [Fact]
    public async Task A_failed_save_leaves_the_editor_open()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Movie());
        Fail(HttpMethod.Post, "/Items/" + ItemId);

        Enter("#txtName", "Third Film Redux");
        await Submit("form.editItemMetadataForm");

        Assert.Equal(new[] { "POST /Items/" + ItemId }, Writes);
        Assert.Empty(Toasts);
        Assert.False(IsLoading);
        Assert.False(pending.IsCompleted);
        Assert.Equal("Third Film Redux", Val("#txtName"));
    }

    [Fact]
    public async Task The_content_type_of_a_folder_is_saved_when_it_was_changed()
    {
        SignIn(admin: true);
        var info = Info(info =>
        {
            info["ContentType"] = "movies";
            info["ContentTypeOptions"] = JsonNode.Parse("""[{"Name":"Inherit","Value":""},{"Name":"Movies","Value":"movies"},{"Name":"Shows","Value":"tvshows"}]""");
        });
        Server.OnPost($"/Items/{ItemId}/ContentType");
        var pending = OpenEditor(Item("Folder"), info);

        Assert.False(IsHidden("#fldContentType"));
        Assert.Equal(new[] { "", "movies", "tvshows" }, Options("#selectContentType"));
        Assert.Equal(new[] { "Inherit", "Movies", "Shows" }, Texts("#selectContentType option"));
        Assert.Equal("movies", Val("#selectContentType"));

        Pick("#selectContentType", "tvshows");
        await Submit("form.editItemMetadataForm");
        await pending;

        Assert.Equal(
            new[] { "/Items/" + ItemId, $"/Items/{ItemId}/ContentType?ContentType=tvshows" },
            Server.Requests.Where(request => request.Method == HttpMethod.Post).Select(request => request.Uri.PathAndQuery));
        Assert.Equal(new[] { T("MessageItemSaved") }, Toasts);
    }

    [Fact]
    public async Task An_unchanged_content_type_is_not_saved()
    {
        SignIn(admin: true);
        var info = Info(info =>
        {
            info["ContentType"] = "movies";
            info["ContentTypeOptions"] = JsonNode.Parse("""[{"Name":"Movies","Value":"movies"},{"Name":"Shows","Value":"tvshows"}]""");
        });
        var pending = OpenEditor(Item("Folder"), info);

        Pick("#selectContentType", "tvshows");
        Pick("#selectContentType", "movies");
        await Submit("form.editItemMetadataForm");
        await pending;

        Assert.Equal(new[] { "POST /Items/" + ItemId }, Writes);
    }

    [Fact]
    public async Task A_series_has_a_status_air_days_a_runtime_and_a_display_order()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Item("Series", null, item =>
        {
            item["Status"] = "Continuing";
            item["AirDays"] = new JsonArray("Monday", "Friday");
            item["AirTime"] = "20:15";
            item["RunTimeTicks"] = 27000000000;
            item["DisplayOrder"] = "dvd";
            item["EndDate"] = "2010-03-04T00:00:00.0000000Z";
        }));

        Assert.Equal("Continuing", Val("#selectStatus"));
        Assert.Equal(new[] { "", "Continuing", "Ended", "Unreleased" }, Options("#selectStatus"));
        Assert.Equal(
            new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
            FindAll("input.chkAirDay").Select(day => day.GetAttribute("data-day")));
        Assert.Equal(new[] { "Monday", "Friday" }, FindAll("input.chkAirDay").Where(day => day.IsChecked()).Select(day => day.GetAttribute("data-day")));
        Assert.Equal("20:15", Val("#txtAirTime"));
        Assert.Equal("45", Val("#txtSeriesRuntime"));
        Assert.Equal("dvd", Val("#selectDisplayOrder"));
        Assert.Equal(
            new[] { "", "originalAirDate", "absolute", "dvd", "digital", "storyArc", "production", "tv", "alternate", "regional", "altdvd" },
            Options("#selectDisplayOrder"));
        Assert.False(IsHidden(".seriesDisplayOrderDescription"));
        Assert.Equal("2010-03-04", Val("#txtEndDate"));
        Assert.Contains("Runtime", FindAll("input.selectLockedField").Select(field => field.GetAttribute("data-value")));

        Pick("#selectStatus", "Ended");
        Check("input.chkAirDay[data-day=Sunday]", true);
        Check("input.chkAirDay[data-day=Friday]", false);
        Enter("#txtAirTime", "21:00");
        Enter("#txtSeriesRuntime", "50");
        Pick("#selectDisplayOrder", "absolute");
        var body = await Save();
        await pending;

        Assert.Equal("Ended", body.Text("Status"));
        Assert.Equal(new[] { "Sunday", "Monday" }, Strings(body.GetProperty("AirDays")));
        Assert.Equal("21:00", body.Text("AirTime"));
        Assert.Equal(30000000000, body.GetProperty("RunTimeTicks").GetInt64());
        Assert.Equal("absolute", body.Text("DisplayOrder"));
        Assert.Equal("2010-03-04T00:00:00.000Z", body.Text("EndDate"));
        Assert.False(body.TryGetProperty("ProductionLocations", out _));
    }

    [Fact]
    public async Task A_series_without_a_runtime_is_saved_without_one()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Item("Series"));

        Assert.Equal("", Val("#txtSeriesRuntime"));
        var body = await Save();
        await pending;

        Assert.Equal(JsonValueKind.Null, body.GetProperty("RunTimeTicks").ValueKind);
    }

    [Fact]
    public void A_collection_is_ordered_by_date_name_or_release()
    {
        SignIn(admin: true);

        _ = OpenEditor(Item("BoxSet", null, item => item["DisplayOrder"] = "SortName"));

        Assert.Equal(new[] { "Default", "SortName", "PremiereDate" }, Options("#selectDisplayOrder"));
        Assert.Equal(new[] { T("DateModified"), T("SortName"), T("ReleaseDate") }, Texts("#selectDisplayOrder option"));
        Assert.Equal("SortName", Val("#selectDisplayOrder"));
        Assert.True(IsHidden(".seriesDisplayOrderDescription"));
    }

    [Fact]
    public async Task A_person_has_a_place_of_birth()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Item("Person", null, item => item["ProductionLocations"] = new JsonArray("Vienna", "Austria")));

        Assert.Equal("Vienna", Val("#txtPlaceOfBirth"));
        Assert.Equal(
            new[] { T("Name"), T("Overview"), T("Genres"), T("ParentalRating"), T("People"), T("BirthLocation"), T("Studios"), T("Tags") },
            Texts(".providerSettingsContainer .checkboxLabel"));

        Enter("#txtPlaceOfBirth", "Graz");
        var body = await Save();
        await pending;
        Assert.Equal(new[] { "Graz" }, Strings(body.GetProperty("ProductionLocations")));
        Assert.False(body.TryGetProperty("RunTimeTicks", out _));
    }

    [Fact]
    public async Task A_person_without_a_place_of_birth_is_saved_with_none()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Item("Person"));

        var body = await Save();
        await pending;

        Assert.Equal(0, body.GetProperty("ProductionLocations").GetArrayLength());
    }

    [Fact]
    public async Task A_special_episode_says_where_it_airs()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Item("Episode", "Video", item =>
        {
            item["ParentIndexNumber"] = 0;
            item["IndexNumber"] = 3;
            item["AirsBeforeSeasonNumber"] = 2;
            item["AirsBeforeEpisodeNumber"] = 0;
        }));

        Assert.False(IsHidden("#collapsibleSpecialEpisodeInfo"));
        Assert.Equal("0", Val("#txtParentIndexNumber"));
        Assert.Equal("3", Val("#txtIndexNumber"));
        Assert.Equal("2", Val("#txtAirsBeforeSeason"));
        Assert.Equal("", Val("#txtAirsAfterSeason"));
        Assert.Equal("0", Val("#txtAirsBeforeEpisode"));

        Enter("#txtAirsAfterSeason", "1");
        Enter("#txtIndexNumber", "");
        var body = await Save();
        await pending;

        Assert.Equal("0", body.Text("ParentIndexNumber"));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("IndexNumber").ValueKind);
        Assert.Equal("2", body.Text("AirsBeforeSeasonNumber"));
        Assert.Equal("1", body.Text("AirsAfterSeasonNumber"));
        Assert.Equal("0", body.Text("AirsBeforeEpisodeNumber"));
    }

    [Fact]
    public void An_episode_of_a_season_has_no_special_episode_fields()
    {
        SignIn(admin: true);

        _ = OpenEditor(Item("Episode", "Video", item => item["ParentIndexNumber"] = 1));

        Assert.True(IsHidden("#collapsibleSpecialEpisodeInfo"));
    }

    [Fact]
    public async Task The_artists_of_a_song_are_separated_by_semicolons()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Item("Audio", "Audio", item =>
        {
            item["Album"] = "First Album";
            item["IndexNumber"] = 4;
            item["ParentIndexNumber"] = 1;
            item["ArtistItems"] = JsonNode.Parse("""[{"Name":"One","Id":"a1"},{"Name":"Two","Id":"a2"}]""");
            item["AlbumArtists"] = JsonNode.Parse("""[{"Name":"Band","Id":"a3"}]""");
        }));

        Assert.Equal("One;Two", Val("#txtArtist"));
        Assert.Equal("Band", Val("#txtAlbumArtist"));
        Assert.Equal("First Album", Val("#txtAlbum"));
        Assert.Equal("4", Val("#txtIndexNumber"));
        Assert.Equal("1", Val("#txtParentIndexNumber"));

        Enter("#txtArtist", "One;Three;");
        Enter("#txtAlbumArtist", "");
        var body = await Save();
        await pending;

        Assert.Equal("""[{"Name":"One"},{"Name":"Three"}]""", Normalized(body.GetProperty("ArtistItems").GetRawText()));
        Assert.Equal(0, body.GetProperty("AlbumArtists").GetArrayLength());
        Assert.Equal("First Album", body.Text("Album"));
        Assert.Equal("4", body.Text("IndexNumber"));
        Assert.Equal("1", body.Text("ParentIndexNumber"));
    }

    [Fact]
    public async Task The_series_of_a_book_is_saved()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Item("Book", null, item => item["SeriesName"] = "A Saga"));

        Assert.Equal("A Saga", Val("#txtseriesName"));
        Enter("#txtseriesName", "Another Saga");
        var body = await Save();
        await pending;

        Assert.Equal("Another Saga", body.Text("SeriesName"));
    }

    [Fact]
    public void A_rating_the_server_does_not_list_can_still_be_kept()
    {
        SignIn(admin: true);

        _ = OpenEditor(Item("Movie", "Video", item => item["OfficialRating"] = "TV-MA"));

        Assert.Equal(new[] { "", "G", "PG-13", "TV-MA" }, Options("#selectOfficialRating"));
        Assert.Equal("TV-MA", Val("#selectOfficialRating"));
        Assert.Equal(new[] { "", "G", "PG-13" }, Options("#selectCustomRating"));
    }

    [Fact]
    public void A_channel_has_a_resolution()
    {
        SignIn(admin: true);

        _ = OpenEditor(Item("TvChannel", "Video", item => item["Height"] = 720));

        Assert.Equal(new[] { "0", "480", "576", "720", "1080", "2160" }, Options("#selectHeight"));
        Assert.Equal("720", Val("#selectHeight"));
    }

    [Fact]
    public async Task Genres_tags_and_studios_are_added_through_a_prompt_and_removed_from_their_lists()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Movie());

        await Click("#genresCollapsible .btnAddTextItem");
        Eventually(() => Assert.Equal("Value:", Label("txtInput")));
        Enter("#txtInput", "comedy");
        await Settle(() => Find("#txtInput").Closest("form")!.SubmitAsync());
        Eventually(() => Assert.Equal(new[] { "Action", "comedy", "Drama" }, Texts("#listGenres .textValue")));

        await Click("#listGenres .btnRemoveFromEditorList[data-index='0']");
        Assert.Equal(new[] { "comedy", "Drama" }, Texts("#listGenres .textValue"));

        await Click("#tagsCollapsible .btnAddTextItem");
        Eventually(() => Assert.NotEmpty(FindAll("#txtInput")));
        Enter("#txtInput", "gamma");
        await Settle(() => Find("#txtInput").Closest("form")!.SubmitAsync());
        Eventually(() => Assert.Equal(new[] { "Alpha", "beta", "gamma" }, Texts("#listTags .textValue")));

        await Click("#listStudios .btnRemoveFromEditorList[data-index='1']");
        await Click("#studiosCollapsible .btnAddTextItem");
        Eventually(() => Assert.NotEmpty(FindAll("#txtInput")));
        Dialogs.Dialogs[^1].Cancel();
        Eventually(() => Assert.Single(Dialogs.Dialogs));
        Assert.Equal(new[] { "Studio A" }, Texts("#listStudios .textValue"));

        var body = await Save();
        await pending;
        Assert.Equal(new[] { "comedy", "Drama" }, Strings(body.GetProperty("Genres")));
        Assert.Equal(new[] { "Alpha", "beta", "gamma" }, Strings(body.GetProperty("Tags")));
        Assert.Equal("""[{"Name":"Studio A"}]""", Normalized(body.GetProperty("Studios").GetRawText()));
    }

    [Fact]
    public async Task People_are_added_edited_and_removed()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Movie());

        // Editing: a director has no role, an actor has one.
        await Click("#peopleList .btnEditPerson[data-index='1']");
        Eventually(() => Assert.Equal("Dan Director", Val(".txtPersonName")));
        Assert.Equal(T("Edit"), DialogTitle);
        Assert.Equal(PersonEditor.PersonTypes.Cast<string?>(), Options("#selectPersonType"));
        Assert.DoesNotContain("Unknown", PersonEditor.PersonTypes);
        Assert.Equal("Director", Val("#selectPersonType"));
        Assert.True(IsHidden(".fldRole"));
        Pick("#selectPersonType", "GuestStar");
        Assert.False(IsHidden(".fldRole"));
        Pick("#selectPersonType", "Actor");
        Assert.False(IsHidden(".fldRole"));
        Enter(".txtPersonRole", "Villain");
        await Submit("form.popupEditPersonForm");
        Eventually(() => Assert.Equal(new[] { "Hero", "Villain" }, Texts("#peopleList .secondary")));

        // Adding: the first kind of person is preselected.
        await Click("#btnAddPerson");
        Eventually(() => Assert.Equal(PersonEditor.PersonTypes[0], Val("#selectPersonType")));
        Assert.Equal("", Val(".txtPersonName"));
        Assert.True(Find(".txtPersonName").HasAttribute("required"));
        Enter(".txtPersonName", "Carl Composer");
        Pick("#selectPersonType", "Composer");
        Assert.True(IsHidden(".fldRole"));
        await Submit("form.popupEditPersonForm");
        Eventually(() => Assert.Equal(new[] { "Ann Actor", "Dan Director", "Carl Composer" }, Texts("#peopleList .textValue")));
        Assert.Equal(new[] { "Hero", "Villain", T("Composer") }, Texts("#peopleList .secondary"));

        // A person editor that is closed adds nobody.
        await Click("#btnAddPerson");
        Eventually(() => Assert.Equal(2, Dialogs.Dialogs.Count));
        Dialogs.Dialogs[^1].Cancel();
        Eventually(() => Assert.Single(Dialogs.Dialogs));
        Assert.Equal(3, FindAll("#peopleList .listItem").Count);

        await Click("#peopleList .btnDeletePerson[data-index='0']");
        Assert.Equal(new[] { "Dan Director", "Carl Composer" }, Texts("#peopleList .textValue"));

        var body = await Save();
        await pending;
        var people = body.GetProperty("People").EnumerateArray().ToArray();
        Assert.Equal(2, people.Length);
        Assert.Equal("Dan Director", people[0].Text("Name"));
        Assert.Equal(TestItems.Id(12), people[0].Text("Id"));
        Assert.Equal("Actor", people[0].Text("Type"));
        Assert.Equal("Villain", people[0].Text("Role"));
        Assert.Equal("Carl Composer", people[1].Text("Name"));
        Assert.Equal("Composer", people[1].Text("Type"));
        Assert.Equal(JsonValueKind.Null, people[1].GetProperty("Role").ValueKind);
    }

    [Fact]
    public void External_ids_have_a_field_each()
    {
        SignIn(admin: true);

        _ = OpenEditor(Movie());

        Assert.False(IsHidden(".externalIdsSection"));
        Assert.Equal(new[] { "txt1Tmdb", "txt1Imdb", "txt1TmdbCollection" }, FindAll(".txtExternalId").Select(input => input.Id));
        Assert.Equal(new[] { "Tmdb", "Imdb", "TmdbCollection" }, FindAll(".txtExternalId").Select(input => input.GetAttribute("data-providerkey")));
        Assert.Equal(new[] { "42", "tt0000042", "" }, FindAll(".txtExternalId").Select(input => input.Value() ?? ""));
        Assert.Equal(T("LabelDynamicExternalId", "IMDb"), Label("txt1Imdb"));
        Assert.Equal(T("LabelDynamicExternalId", "TheMovieDb " + T("BoxSet")), Label("txt1TmdbCollection"));
    }

    [Fact]
    public void An_item_without_external_ids_has_no_section_for_them()
    {
        SignIn(admin: true);

        _ = OpenEditor(Movie(), Info(info => info["ExternalIdInfos"] = new JsonArray()));

        Assert.True(IsHidden(".externalIdsSection"));
        Assert.Empty(FindAll(".txtExternalId"));
    }

    [Fact]
    public async Task Fields_are_locked_one_by_one_or_all_at_once()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Movie());

        Assert.False(Find("#chkLockData").IsChecked());
        Assert.False(IsHidden(".providerSettingsContainer"));
        Assert.Equal(
            new[] { "Name", "Overview", "Genres", "OfficialRating", "Cast", "ProductionLocations", "Studios", "Tags" },
            FindAll("input.selectLockedField").Select(field => field.GetAttribute("data-value")));

        // A ticked field is one that providers may change.
        Assert.Equal(
            new[] { "Name", "Overview", "OfficialRating", "ProductionLocations", "Studios", "Tags" },
            FindAll("input.selectLockedField").Where(field => field.IsChecked()).Select(field => field.GetAttribute("data-value")));

        Check("input.selectLockedField[data-value=Name]", false);
        Check("input.selectLockedField[data-value=Genres]", true);
        Check("input.selectLockedField[data-value=Tags]", false);
        Check("#chkLockData", true);
        Assert.True(IsHidden(".providerSettingsContainer"));

        var body = await Save();
        await pending;
        Assert.True(body.Flag("LockData"));
        Assert.Equal(new[] { "Name", "Cast", "Tags" }, Strings(body.GetProperty("LockedFields")));
    }

    [Fact]
    public async Task Reset_empties_the_editor()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Movie(), Info());
        Check("#chkLockData", true);

        await Click(".btnReset");

        foreach (var field in MetadataEditorLogic.ResetFields)
        {
            Assert.Equal(string.Empty, Val("#" + field));
        }

        Assert.Equal(Path, Val("#txtPath"));
        Assert.Empty(FindAll("#listGenres .listItem"));
        Assert.Empty(FindAll("#listTags .listItem"));
        Assert.Empty(FindAll("#listStudios .listItem"));
        Assert.Empty(FindAll("#peopleList .listItem"));
        Assert.All(FindAll(".txtExternalId"), input => Assert.Equal(string.Empty, input.Value() ?? string.Empty));
        Assert.False(Find("#chkLockData").IsChecked());
        Assert.False(IsHidden(".providerSettingsContainer"));
        Assert.All(FindAll("input.selectLockedField"), field => Assert.True(field.IsChecked()));
        Assert.Empty(Writes);

        var body = await Save();
        await pending;
        Assert.Equal("", body.Text("Name"));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("PremiereDate").ValueKind);
        Assert.Equal(0, body.GetProperty("Genres").GetArrayLength());
        Assert.Equal(0, body.GetProperty("People").GetArrayLength());
        Assert.Equal(0, body.GetProperty("LockedFields").GetArrayLength());
        Assert.False(body.Flag("LockData"));
        Assert.Equal("""{"Tmdb":"","Imdb":"","Legacy":"x","TmdbCollection":""}""", Normalized(body.GetProperty("ProviderIds").GetRawText()));
    }

    [Fact]
    public void The_air_days_of_a_series_are_reset_too()
    {
        SignIn(admin: true);
        _ = OpenEditor(Item("Series", null, item => item["AirDays"] = new JsonArray("Monday")));
        Assert.Single(FindAll("input.chkAirDay"), day => day.IsChecked());

        Find(".btnReset").Click();

        Assert.DoesNotContain(FindAll("input.chkAirDay"), day => day.IsChecked());
    }

    [Theory]
    [InlineData("desktop", true, false)]
    [InlineData("mobile", false, true)]
    public void The_header_has_a_back_button_on_phones_and_a_close_button_on_desktops(string layout, bool backHidden, bool closeHidden)
    {
        SignIn(admin: true);
        Get<AppHost>().SetLayout(layout);

        _ = OpenEditor(Movie());

        Assert.Equal(backHidden, IsHidden(".btnBack"));
        Assert.Equal(closeHidden, IsHidden(".btnClose"));
    }

    [Fact]
    public async Task The_more_menu_opens_at_its_button_with_the_other_editors_of_the_item()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Movie());

        await Click(".btnMore");
        Eventually(() => Assert.NotEmpty(FindAll("button.actionSheetMenuItem")));

        var commands = FindAll("button.actionSheetMenuItem").Select(command => command.GetAttribute("data-id")).ToArray();
        Assert.Contains("editimages", commands);
        Assert.Contains("identify", commands);
        Assert.Contains("refresh", commands);
        Assert.Contains("delete", commands);
        Assert.DoesNotContain("edit", commands);
        Assert.DoesNotContain("share", commands);
        Assert.DoesNotContain("resume", commands);
        Assert.DoesNotContain("queue", commands);

        // The sheet is placed at the button, not at the card that was clicked last.
        var anchor = Assert.Single(Positioner.Calls).PositionTo;
        Assert.NotNull(anchor);
        Assert.NotNull(anchor.Element);

        Dialogs.Dialogs[^1].Cancel();
        Eventually(() => Assert.Single(Dialogs.Dialogs));
        Assert.False(pending.IsCompleted);
        Assert.Equal(2, Server.Requests.Count);
    }

    [Fact]
    public async Task Deleting_the_item_from_the_more_menu_shows_its_parent()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Movie());
        var folder = Item("Folder", null, item =>
        {
            item["Id"] = ParentId;
            item["Name"] = "Movies";
        });
        Have(folder);
        Server.OnDelete("/Items/" + ItemId);

        await Click(".btnMore");
        Eventually(() => Assert.NotEmpty(FindAll("button.actionSheetMenuItem[data-id=delete]")));
        await Click("button.actionSheetMenuItem[data-id=delete]");
        Eventually(() => Assert.NotEmpty(FindAll("button.btnOption[data-id=ok]")));
        await Answer("ok");

        Eventually(() => Assert.Equal("Movies", Val("#txtName")));
        Assert.Equal(new[] { "DELETE /Items/" + ItemId }, Writes);
        Assert.Equal($"/Items/{ParentId}/MetadataEditor", Server.Requests.Last(request => request.Path.EndsWith("/MetadataEditor", StringComparison.Ordinal)).Path);
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task Deleting_an_item_without_a_parent_closes_the_editor_and_goes_home()
    {
        SignIn(admin: true);
        var movie = Movie();
        movie.Remove("ParentId");
        var pending = OpenEditor(movie);
        Server.OnDelete("/Items/" + ItemId);

        await Click(".btnMore");
        Eventually(() => Assert.NotEmpty(FindAll("button.actionSheetMenuItem[data-id=delete]")));
        await Click("button.actionSheetMenuItem[data-id=delete]");
        Eventually(() => Assert.NotEmpty(FindAll("button.btnOption[data-id=ok]")));
        await Answer("ok");

        await pending.WaitAsync(PageTestExtensions.PatienceLimit);
        Assert.Equal("/home", App.Browser.Hash);
        Assert.Empty(Dialogs.Dialogs);
    }

    [Fact]
    public async Task The_editor_is_opened_by_the_edit_command()
    {
        SignIn(admin: true);
        Have(Movie());
        var handlers = Get<ItemCommandHandlers>();
        var item = TestItems.Item(new { Id = ItemId, Type = "Movie", Name = "Third Film" });
        Assert.True(handlers.CanHandle(ItemCommand.Edit, item));

        var pending = Open(() => handlers.ExecuteAsync(ItemCommand.Edit, item));
        Eventually(() => Assert.Equal("Third Film", Val("#txtName")));
        await Click(".formDialogFooter .btnCancel");

        // Like the original, the caller is told to refresh whether or not something was saved.
        Assert.Equal(new ItemCommandResult(Updated: true), await pending);
    }
}

/// <summary>The more menu of the metadata editor with a command that changes the item.</summary>
public class MetadataEditorMenuTests : MetadataEditorTestContext
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<IItemCommandHandler>(new IdentifyingHandler());
    }

    [Fact]
    public async Task The_item_is_loaded_again_after_a_command_of_the_more_menu_changed_it()
    {
        SignIn(admin: true);
        var pending = OpenEditor(Movie());
        var identified = Movie();
        identified["Name"] = "Third Film (identified)";
        Have(identified);

        await Click(".btnMore");
        Eventually(() => Assert.NotEmpty(FindAll("button.actionSheetMenuItem[data-id=identify]")));
        await Click("button.actionSheetMenuItem[data-id=identify]");

        Eventually(() => Assert.Equal("Third Film (identified)", Val("#txtName")));
        Assert.Equal(2, Server.RequestsTo($"/Users/{UserId}/Items/{ItemId}").Count);
        Assert.Equal(2, Server.RequestsTo($"/Items/{ItemId}/MetadataEditor").Count);
        Assert.False(pending.IsCompleted);
    }

    private sealed class IdentifyingHandler : IItemCommandHandler
    {
        public bool CanHandle(string command, BaseItemDto item) => command == ItemCommand.Identify;

        public Task<ItemCommandResult> ExecuteAsync(string command, BaseItemDto item, ItemActionContext context) =>
            Task.FromResult(new ItemCommandResult(Updated: true));
    }
}
