using System.Net;
using System.Text;

using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>
/// The texts and links of the details page header, built as HTML exactly like the original builds
/// them (<c>renderName</c>, <c>getArtistLinksHtml</c>, <c>renderLinks</c>, <c>renderTags</c> and the
/// person lines of <c>reloadFromItem</c>). Every value from the server is encoded.
/// Links carry real addresses instead of the original's click handlers.
/// </summary>
[AppService]
public sealed class DetailsText(Globalize globalize, AppRouter router, DateTimeText dateTime)
{
    /// <summary>The name block: parent names (series, season, artists, album), the name and the original title.</summary>
    public string GetNameHtml(BaseItemDto item, string? context, bool mobile)
    {
        var type = item.TypeName();
        var parents = new List<string>();
        var parentNameLast = false;

        if (item.AlbumArtists is { } albumArtists)
        {
            parents.Add(GetArtistLinksHtml(albumArtists, item.ServerId, context));
            parentNameLast = true;
        }
        else if (item.ArtistItems is { Count: > 0 } artists && type == "MusicVideo")
        {
            parents.Add(GetArtistLinksHtml(artists, item.ServerId, context));
            parentNameLast = true;
        }
        else if (!string.IsNullOrEmpty(item.SeriesName) && type == "Episode")
        {
            parents.Add(ItemLink(item.SeriesId, item.ServerId, BaseItemDto_Type.Series, item.SeriesName));
        }
        else if (item.IsSeries == true || !string.IsNullOrEmpty(item.EpisodeTitle))
        {
            parents.Add(Encode(item.Name));
        }

        if (!string.IsNullOrEmpty(item.SeriesName) && type == "Season")
        {
            parents.Add(ItemLink(item.SeriesId, item.ServerId, BaseItemDto_Type.Series, item.SeriesName));
        }
        else if (item.ParentIndexNumber is not null && type == "Episode")
        {
            parents.Add(ItemLink(item.SeasonId, item.ServerId, BaseItemDto_Type.Season, item.SeasonName));
        }
        else if (item.ParentIndexNumber is { } seasonNumber && item.IsSeries == true)
        {
            parents.Add(Encode(string.IsNullOrEmpty(item.SeasonName) ? "S" + DetailsRules.Invariant(seasonNumber) : item.SeasonName));
        }
        else if (!string.IsNullOrEmpty(item.Album) && item.AlbumId is not null && type is "MusicVideo" or "Audio")
        {
            parents.Add(ItemLink(item.AlbumId, item.ServerId, BaseItemDto_Type.MusicAlbum, item.Album));
        }
        else if (!string.IsNullOrEmpty(item.Album))
        {
            parents.Add(Encode(item.Album));
        }

        var html = string.Empty;
        if (parents.Count > 0)
        {
            if (parentNameLast)
            {
                html = mobile
                    ? "<h3 class=\"parentName musicParentName\">" + string.Join("</br>", parents) + "</h3>"
                    : "<h3 class=\"parentName musicParentName focuscontainer-x\">" + string.Join(" - ", parents) + "</h3>";
            }
            else
            {
                html = "<h1 class=\"parentName focuscontainer-x\"><bdi>" + parents[0] + "</bdi></h1>";
            }
        }

        var name = Encode(ItemHelper.GetDisplayName(item, globalize, includeParentInfo: false));
        var hasOriginalTitle = !string.IsNullOrEmpty(item.OriginalTitle) && item.OriginalTitle != item.Name;

        if (html.Length > 0 && !parentNameLast)
        {
            html += parents.Count > 1 && parents[1].Length > 0
                ? "<h3 class=\"itemName infoText subtitle focuscontainer-x\"><bdi>" + parents[1] + " - " + name + "</bdi></h3>"
                : "<h3 class=\"itemName infoText subtitle\"><bdi>" + name + "</bdi></h3>";
        }
        else if (hasOriginalTitle)
        {
            html = "<h1 class=\"itemName infoText parentNameLast withOriginalTitle\"><bdi>" + name + "</bdi></h1>" + html;
        }
        else
        {
            html = "<h1 class=\"itemName infoText parentNameLast\"><bdi>" + name + "</bdi></h1>" + html;
        }

        if (hasOriginalTitle)
        {
            html += "<h4 class=\"itemName infoText originalTitle\">" + Encode(item.OriginalTitle) + "</h4>";
        }

        return html;
    }

    /// <summary>Links to the first ten artists, and "and N others" for the rest.</summary>
    public string GetArtistLinksHtml(IReadOnlyList<NameGuidPair> artists, string? serverId, string? context)
    {
        var links = artists.Take(10).Select(artist =>
        {
            var href = router.GetRouteUrl(
                new BaseItemDto { Id = artist.Id, Name = artist.Name, ServerId = serverId, Type = BaseItemDto_Type.MusicArtist },
                new RouteOptions { Context = context, ServerId = serverId });
            return "<a style=\"color:inherit;\" class=\"button-link emby-button\" href=\"" + Encode(href) + "\">" + Encode(artist.Name) + "</a>";
        });

        var html = string.Join(" / ", links);
        return artists.Count > 10 ? globalize.Translate("AndOtherArtists", html, artists.Count - 10) : html;
    }

    /// <summary>The links to the item on other sites; empty when it has none.</summary>
    public string GetExternalLinksHtml(BaseItemDto item)
    {
        // PORT-TODO(details): the original puts the item's own website (HomePageUrl) first; the
        // server API model of this client has no such property.
        var links = (item.ExternalUrls ?? [])
            .Where(link => IsWebAddress(link.Url))
            .Select(link => "<a class=\"button-link emby-button\" href=\"" + Encode(link.Url) + "\" target=\"_blank\" rel=\"noopener noreferrer\">" + Encode(link.Name) + "</a>");
        return string.Join(", ", links);
    }

    /// <summary>The tags of the item as links to the items sharing them; empty for programmes and items without tags.</summary>
    public string GetTagsHtml(BaseItemDto item)
    {
        if (item.TypeName() == "Program" || item.Tags is not { Count: > 0 } tags)
        {
            return string.Empty;
        }

        var links = tags.Select(tag => "<a href=\"" + Encode(router.GetTagUrl(tag)) + "\" class=\"button-link emby-button\">" + Encode(tag) + "</a>");
        return globalize.Translate("TagsValue", string.Join(", ", links));
    }

    /// <summary>"Born: date" with the age of a living person; null for other items.</summary>
    public string? GetBirthdayText(BaseItemDto item, DateTimeOffset now)
    {
        if (item.TypeName() != "Person" || item.PremiereDate is not { } birthday)
        {
            return null;
        }

        var text = globalize.Translate("BirthDateValue", dateTime.ToLocaleDateString(birthday));
        return item.EndDate is null
            ? text + " " + globalize.Translate("AgeValue", DetailsRules.YearsBetween(birthday, now))
            : text;
    }

    /// <summary>"Died: date" with the age reached; null for other items.</summary>
    public string? GetDeathDateText(BaseItemDto item)
    {
        if (item.TypeName() != "Person" || item.EndDate is not { } deathday)
        {
            return null;
        }

        var text = globalize.Translate("DeathDateValue", dateTime.ToLocaleDateString(deathday));
        return item.PremiereDate is { } birthday
            ? text + " " + globalize.Translate("AgeValue", DetailsRules.YearsBetween(birthday, deathday))
            : text;
    }

    /// <summary>"Birth place: place" with a link to a map; null for other items.</summary>
    public string? GetBirthPlaceHtml(BaseItemDto item)
    {
        if (item.TypeName() != "Person" || item.ProductionLocations is not { Count: > 0 } locations)
        {
            return null;
        }

        var location = locations[0];
        var link = "<a class=\"button-link textlink emby-button\" target=\"_blank\" rel=\"noopener noreferrer\" href=\"https://www.openstreetmap.org/search?query="
            + Uri.EscapeDataString(location) + "\">" + Encode(location) + "</a>";
        return globalize.Translate("BirthPlaceValue", link);
    }

    internal static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private static bool IsWebAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private string ItemLink(Guid? id, string? serverId, BaseItemDto_Type type, string? text)
    {
        var target = new BaseItemDto { Id = id, ServerId = serverId, Type = type, IsFolder = true };
        var html = new StringBuilder("<a style=\"color:inherit;\" class=\"button-link itemAction emby-button\" href=\"");
        html.Append(Encode(router.GetRouteUrl(target)))
            .Append("\" data-action=\"link\" data-id=\"").Append(id.IdText())
            .Append("\" data-serverid=\"").Append(Encode(serverId))
            .Append("\" data-type=\"").Append(type.ToApiString())
            .Append("\" data-isfolder=\"true\">").Append(Encode(text)).Append("</a>");
        return html.ToString();
    }
}
