using System.Text.Json.Nodes;
using Jellyfin.Web.Services;
using Microsoft.AspNetCore.Components;

namespace Jellyfin.Web.Components;

/// <summary>
/// A node of the metadata manager's library tree. Port of the node objects that
/// scripts/editorsidebar.js hands to jstree (<c>getNode()</c> and the two fixed nodes of
/// <c>loadChildrenOfRootNode()</c>).
/// </summary>
public sealed class MetadataTreeNode
{
    /// <summary>The id of the fixed node that holds the media folders.</summary>
    public const string MediaFoldersId = "MediaFolders";

    /// <summary>The id of the fixed node that holds the Live TV channels.</summary>
    public const string LiveTvId = "livetv";

    /// <summary>The item's id, or the id of a fixed node.</summary>
    public required string Id { get; init; }

    /// <summary>"mediafolders" or "livetv" for the fixed nodes (<c>li_attr.itemtype</c>); null for items.</summary>
    public string? ItemType { get; init; }

    /// <summary>The item's type (<c>li_attr.serveritemtype</c>).</summary>
    public string? ServerItemType { get; init; }

    /// <summary>The item's collection type (<c>li_attr.collectiontype</c>).</summary>
    public string? CollectionType { get; init; }

    /// <summary>The label: the item's name with its number in front, or the fixed node's title.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The Material icon in front of an item's name; null for none.</summary>
    public string? Icon { get; set; }

    /// <summary>Whether the item's metadata is locked (a lock icon in front of the name).</summary>
    public bool Locked { get; set; }

    /// <summary>Whether the node can hold children (the original gives it a "Loading..." child).</summary>
    public bool IsFolder { get; init; }

    public bool Opened { get; set; }

    /// <summary>Whether the children have been requested (<c>li_attr.loadedFromServer</c>).</summary>
    public bool LoadedFromServer { get; set; }

    /// <summary>Whether the request for the children is under way.</summary>
    public bool Loading { get; set; }

    /// <summary>The children; null while only the "Loading..." placeholder is there.</summary>
    public List<MetadataTreeNode>? Children { get; set; }

    public MetadataTreeNode? Parent { get; set; }

    /// <summary>Whether this is an item that the editor can show, rather than one of the fixed nodes.</summary>
    public bool IsItem => ItemType is null;

    /// <summary>jstree shows a node without children as a leaf: no toggle.</summary>
    public bool IsLeaf => !IsFolder || Children is { Count: 0 };

    /// <summary>The item the node was built from; null for the fixed nodes.</summary>
    internal JsonObject? Source { get; init; }

    /// <summary>The node's link element, for moving the focus to it.</summary>
    internal ElementReference Anchor { get; set; }

    /// <summary>Port of <c>getNode(item, folderState, selected)</c>.</summary>
    public static MetadataTreeNode FromItem(JsonNode? item, bool opened)
    {
        var isFolder = item.Flag("IsFolder");
        var open = isFolder && opened;
        return new MetadataTreeNode
        {
            Id = item.Text("Id") ?? string.Empty,
            Source = item as JsonObject,
            ServerItemType = item.Text("Type"),
            CollectionType = item.Text("CollectionType"),
            Text = Label(item),
            Icon = IconOf(item),
            Locked = item.Flag("LockData"),
            IsFolder = isFolder,
            Opened = open,
            LoadedFromServer = open
        };
    }

    /// <summary>The name part of <c>getNodeInnerHtml(item)</c>: channel and index numbers go in front.</summary>
    public static string Label(JsonNode? item)
    {
        var name = item.Text("Name") ?? string.Empty;
        if (item.Text("Number") is { Length: > 0 } number)
        {
            name = number + " - " + name;
        }

        if (item.Text("IndexNumber") is { } indexNumber && item.Text("Type") != "Season")
        {
            name = indexNumber + " - " + name;
        }

        return name;
    }

    /// <summary>The icon part of <c>getNodeInnerHtml(item)</c>.</summary>
    public static string? IconOf(JsonNode? item)
    {
        if (item.Flag("IsFolder"))
        {
            return "folder";
        }

        var mediaType = item.Text("MediaType");
        if (mediaType == "Video")
        {
            return "movie";
        }

        if (mediaType == "Audio")
        {
            return "audiotrack";
        }

        if (item.Text("Type") == "TvChannel")
        {
            return "live_tv";
        }

        return mediaType switch
        {
            "Photo" => "photo",
            "Book" => "book",
            _ => null
        };
    }
}
