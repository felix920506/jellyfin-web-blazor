using System.Runtime.CompilerServices;

using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>
/// Values the original client keeps on its item objects that the SDK's <see cref="BaseItemDto"/>
/// has no property for (timers posing as items, playlist position, client-side sync state…).
/// Attached to an item with <see cref="ItemExtensions.Extras"/>; absent for ordinary items.
/// </summary>
public sealed class ItemExtras
{
    /// <summary>Overrides the item type: "Timer" or "SeriesTimer" (the SDK enum has neither).</summary>
    public string? TypeName { get; set; }

    /// <summary>The programme a Live TV timer records (<c>item.ProgramInfo</c>).</summary>
    public BaseItemDto? ProgramInfo { get; set; }

    /// <summary>Image of a timer's parent (<c>PrimaryImageTag</c> / <c>PrimaryImageItemId</c>).</summary>
    public string? PrimaryImageTag { get; set; }

    public string? PrimaryImageItemId { get; set; }

    /// <summary>Position in the playlist the item was loaded from, and that playlist's size.</summary>
    public int? PlaylistIndex { get; set; }

    public int? PlaylistItemCount { get; set; }

    /// <summary>Series timer rules.</summary>
    public bool? RecordAnyTime { get; set; }

    public bool? RecordAnyChannel { get; set; }

    /// <summary>Offline sync progress (0–100) where the host supports downloads.</summary>
    public double? SyncPercent { get; set; }

    /// <summary>Role and kind of a person shown as a card (<c>BaseItemPerson.Role</c> / <c>.Type</c>).</summary>
    public string? Role { get; set; }

    public string? PersonType { get; set; }

    /// <summary>All roles of a person who appears several times in a cast list (<c>RoleList</c>).</summary>
    public IReadOnlyList<ItemPersonRole>? RoleList { get; set; }
}

/// <summary>One credit of a person: the kind ("Actor", "Director"…) and the role played.</summary>
public sealed record ItemPersonRole(string? Type, string? Role);

/// <summary>Accessors that give <see cref="BaseItemDto"/> the shape the ported item code expects.</summary>
public static class ItemExtensions
{
    private static readonly ConditionalWeakTable<BaseItemDto, ItemExtras> Table = [];

    /// <summary>The extras of an item, or null when none were attached.</summary>
    public static ItemExtras? Extras(this BaseItemDto item) => Table.TryGetValue(item, out var extras) ? extras : null;

    /// <summary>The extras of an item, created on first use.</summary>
    public static ItemExtras EnsureExtras(this BaseItemDto item) => Table.GetValue(item, static _ => new ItemExtras());

    /// <summary>The item type as the server spells it ("Movie", "Episode", "Timer"…), or "".</summary>
    public static string TypeName(this BaseItemDto item) =>
        item.Extras()?.TypeName ?? item.Type.ToApiString() ?? string.Empty;

    /// <summary>The media type ("Video", "Audio", "Photo", "Book"), or null when unknown.</summary>
    public static string? MediaTypeName(this BaseItemDto item) =>
        item.MediaType is null or BaseItemDto_MediaType.Unknown ? null : item.MediaType.ToApiString();

    /// <summary>The collection type ("movies", "tvshows"…), or null.</summary>
    public static string? CollectionTypeName(this BaseItemDto item) =>
        item.CollectionType is null or BaseItemDto_CollectionType.Unknown ? null : item.CollectionType.ToApiString();

    /// <summary>The id in the "N" format every URL of the client uses, or "".</summary>
    public static string IdText(this BaseItemDto item) => item.Id.IdText();

    public static string IdText(this Guid? id) => id?.ToString("N") ?? string.Empty;

    /// <summary>The tag of one of the item's own images ("Primary", "Thumb", "Banner"…), or null.</summary>
    public static string? ImageTag(this BaseItemDto item, string imageType) =>
        item.ImageTags?.AdditionalData is { } tags && tags.TryGetValue(imageType, out var tag) ? tag?.ToString() : null;

    /// <summary>The blurhash of an image, looked up by type and tag.</summary>
    public static string? BlurHash(this BaseItemDto item, string imageType, string? tag)
    {
        if (tag is null || item.ImageBlurHashes is not { } hashes)
        {
            return null;
        }

        var byTag = imageType switch
        {
            "Primary" => hashes.Primary?.AdditionalData,
            "Thumb" => hashes.Thumb?.AdditionalData,
            "Backdrop" => hashes.Backdrop?.AdditionalData,
            "Banner" => hashes.Banner?.AdditionalData,
            "Logo" => hashes.Logo?.AdditionalData,
            "Disc" => hashes.Disc?.AdditionalData,
            "Art" => hashes.Art?.AdditionalData,
            _ => null
        };
        return byTag is not null && byTag.TryGetValue(tag, out var hash) ? hash?.ToString() : null;
    }

    /// <summary>True for the Live TV types that are named by programme (Program, Timer, Recording).</summary>
    public static bool UsesLiveTvNaming(this BaseItemDto item) => item.TypeName() is "Program" or "Timer" or "Recording";

    /// <summary>Wraps a Live TV timer as an item of type "Timer", the way the original client treats it.</summary>
    public static BaseItemDto ToItem(this TimerInfoDto timer)
    {
        var item = new BaseItemDto
        {
            Name = timer.Name,
            ServerId = timer.ServerId,
            ChannelId = timer.ChannelId,
            ChannelName = timer.ChannelName,
            ChannelPrimaryImageTag = timer.ChannelPrimaryImageTag,
            StartDate = timer.StartDate,
            EndDate = timer.EndDate,
            Overview = timer.Overview,
            ProgramId = timer.ProgramId,
            RunTimeTicks = timer.RunTimeTicks,
            SeriesTimerId = timer.SeriesTimerId,
            TimerId = timer.Id,
            Status = timer.Status.ToApiString(),
            ParentBackdropImageTags = timer.ParentBackdropImageTags,
            ParentBackdropItemId = Guid.TryParse(timer.ParentBackdropItemId, out var backdropId) ? backdropId : null
        };
        var extras = item.EnsureExtras();
        extras.TypeName = "Timer";
        extras.ProgramInfo = timer.ProgramInfo;
        return item;
    }

    /// <summary>Wraps a Live TV series timer as an item of type "SeriesTimer".</summary>
    public static BaseItemDto ToItem(this SeriesTimerInfoDto timer)
    {
        var item = new BaseItemDto
        {
            Name = timer.Name,
            ServerId = timer.ServerId,
            ChannelId = timer.ChannelId,
            ChannelName = timer.ChannelName,
            ChannelPrimaryImageTag = timer.ChannelPrimaryImageTag,
            StartDate = timer.StartDate,
            EndDate = timer.EndDate,
            Overview = timer.Overview,
            ProgramId = timer.ProgramId,
            SeriesTimerId = timer.Id,
            ParentPrimaryImageItemId = timer.ParentPrimaryImageItemId,
            ParentPrimaryImageTag = timer.ParentPrimaryImageTag,
            ParentThumbImageTag = timer.ParentThumbImageTag,
            ParentThumbItemId = Guid.TryParse(timer.ParentThumbItemId, out var thumbId) ? thumbId : null,
            ParentBackdropImageTags = timer.ParentBackdropImageTags,
            ParentBackdropItemId = Guid.TryParse(timer.ParentBackdropItemId, out var backdropId) ? backdropId : null
        };
        var extras = item.EnsureExtras();
        extras.TypeName = "SeriesTimer";
        extras.RecordAnyTime = timer.RecordAnyTime;
        extras.RecordAnyChannel = timer.RecordAnyChannel;
        return item;
    }

    /// <summary>
    /// The id the cards and menus address the item by: the timer id for timers (which have no item
    /// id), otherwise the item id.
    /// </summary>
    public static string ActionId(this BaseItemDto item) => item.TypeName() switch
    {
        "Timer" => item.TimerId ?? string.Empty,
        "SeriesTimer" => item.SeriesTimerId ?? string.Empty,
        _ => item.IdText()
    };
}
