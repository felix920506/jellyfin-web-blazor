namespace Jellyfin.Web.Services;

/// <summary>
/// What a click on an item element does (<c>constants/itemAction.ts</c>); the values are the
/// original <c>data-action</c> strings. Executed by <see cref="ItemActions"/>.
/// </summary>
public static class ItemAction
{
    /// <summary>Add the item to a playlist.</summary>
    public const string AddToPlaylist = "addtoplaylist";

    /// <summary>Raise <see cref="ItemActionContext.OnCustomAction"/>.</summary>
    public const string Custom = "custom";

    /// <summary>Open an editor for the item.</summary>
    public const string Edit = "edit";

    /// <summary>Play an instant mix based on the item.</summary>
    public const string InstantMix = "instantmix";

    /// <summary>Open the item (details page, library page…).</summary>
    public const string Link = "link";

    /// <summary>Open the context menu.</summary>
    public const string Menu = "menu";

    /// <summary>Do nothing; stops a parent element's action.</summary>
    public const string None = "none";

    public const string Play = "play";

    /// <summary>Play the list of the container, starting with this item.</summary>
    public const string PlayAllFromHere = "playallfromhere";

    public const string PlayMenu = "playmenu";

    public const string PlayTrailer = "playtrailer";

    /// <summary>Open the Live TV programme dialog.</summary>
    public const string ProgramDialog = "programdialog";

    public const string Queue = "queue";

    public const string QueueAllFromHere = "queueallfromhere";

    public const string Record = "record";

    /// <summary>Play from the saved position.</summary>
    public const string Resume = "resume";

    /// <summary>Jump to this entry of the play queue.</summary>
    public const string SetPlaylistIndex = "setplaylistindex";

    /// <summary>
    /// The action of a card (<c>resolveAction</c>): folders are opened even when the list plays on
    /// click, photos always play (the slideshow).
    /// </summary>
    public static string Resolve(string? defaultAction, bool isFolder, bool isPhoto)
    {
        var action = string.IsNullOrEmpty(defaultAction) ? Link : defaultAction;
        if (action == Play && isFolder)
        {
            return Link;
        }

        return isPhoto ? Play : action;
    }
}
