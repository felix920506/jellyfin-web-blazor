using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Services;

/// <summary>
/// Ids of the item commands: the entries of the item context menu (the original ids of
/// <c>itemContextMenu.js</c>) and the card actions that open an editor or dialog.
/// </summary>
public static class ItemCommand
{
    // Handled by the item components themselves.
    public const string Resume = "resume";
    public const string Play = "play";
    public const string PlayAllFromHere = "playallfromhere";
    public const string QueueAllFromHere = "queueallfromhere";
    public const string StopPlayback = "stopPlayback";
    public const string Queue = "queue";
    public const string QueueNext = "queuenext";
    public const string Shuffle = "shuffle";
    public const string InstantMix = "instantmix";
    public const string CopyStream = "copy-stream";
    public const string Delete = "delete";
    public const string RemoveFromPlaylist = "removefromplaylist";
    public const string MoveToTop = "movetotop";
    public const string MoveToBottom = "movetobottom";
    public const string RemoveFromCollection = "removefromcollection";
    public const string Album = "album";
    public const string Artist = "artist";
    public const string Lyrics = "lyrics";

    // Handled by an IItemCommandHandler of the workstream that owns the feature.

    /// <summary>Clear the play queue (playback).</summary>
    public const string ClearQueue = "clearQueue";

    /// <summary>Start multi-selection with this card (library pages).</summary>
    public const string MultiSelect = "multiSelect";

    /// <summary>Collection editor.</summary>
    public const string AddToCollection = "addtocollection";

    /// <summary>Playlist editor: add the item to a playlist.</summary>
    public const string AddToPlaylist = "addtoplaylist";

    /// <summary>Cancel a recording timer, after a confirmation (Live TV).</summary>
    public const string CancelTimer = "canceltimer";

    /// <summary>Cancel a series timer, after a confirmation (Live TV).</summary>
    public const string CancelSeriesTimer = "cancelseriestimer";

    /// <summary>Download every file of a series, season, album or collection.</summary>
    public const string DownloadAll = "downloadall";

    /// <summary>Download the item's file.</summary>
    public const string Download = "download";

    /// <summary>Playlist editor: edit the playlist itself.</summary>
    public const string EditPlaylist = "editplaylist";

    /// <summary>Metadata editor; for items of type Timer the recording editor.</summary>
    public const string Edit = "edit";

    /// <summary>Image editor.</summary>
    public const string EditImages = "editimages";

    /// <summary>Subtitle editor.</summary>
    public const string EditSubtitles = "editsubtitles";

    /// <summary>Lyrics editor.</summary>
    public const string EditLyrics = "editlyrics";

    /// <summary>Item identifier (metadata search).</summary>
    public const string Identify = "identify";

    /// <summary>The media info dialog.</summary>
    public const string MoreMediaInfo = "moremediainfo";

    /// <summary>The recording dialog of a programme (create or manage).</summary>
    public const string Record = "record";

    /// <summary>The refresh metadata dialog.</summary>
    public const string Refresh = "refresh";

    /// <summary>Share the item (Web Share).</summary>
    public const string Share = "share";

    /// <summary>The Live TV programme dialog (card action "programdialog").</summary>
    public const string ProgramDialog = "programdialog";

    /// <summary>Toggle the recording of a programme or timer without a dialog (card action "record").</summary>
    public const string ToggleRecording = "togglerecording";

    /// <summary>The play menu of an item (card action "playmenu").</summary>
    public const string PlayMenu = "playmenu";

    /// <summary>Jump to an entry of the play queue (card action "setplaylistindex").</summary>
    public const string SetPlaylistIndex = "setplaylistindex";
}

/// <summary>What a command did; the list that showed the item refreshes when it changed.</summary>
public sealed record ItemCommandResult(bool Updated = false, bool Deleted = false)
{
    public static readonly ItemCommandResult None = new();
}

/// <summary>
/// Extension point for item commands whose feature lives in another workstream (metadata, image,
/// subtitle and lyrics editors, playlist and collection editors, downloads, recordings, refresh
/// dialog, sharing, multi-select…). Register an implementation with
/// <c>[AppService(typeof(IItemCommandHandler))]</c>; any number may exist, each claiming its own
/// commands. The context menu shows these commands by the original visibility rules whether or
/// not a handler exists; a command nobody handles answers with a "not available yet" toast.
/// </summary>
public interface IItemCommandHandler
{
    /// <summary>Whether this handler implements <paramref name="command"/> (an <see cref="ItemCommand"/> id) for the item.</summary>
    bool CanHandle(string command, BaseItemDto item);

    /// <summary>
    /// Runs the command. <paramref name="context"/> describes the list the item is shown in
    /// (collection, playlist, sibling items).
    /// </summary>
    Task<ItemCommandResult> ExecuteAsync(string command, BaseItemDto item, ItemActionContext context);
}

/// <summary>Finds the <see cref="IItemCommandHandler"/> for a command.</summary>
[AppService]
public sealed class ItemCommandHandlers(IServiceProvider services, ToastService toast)
{
    /// <summary>Shown when a command has no handler yet. Not a translation key: the original has no such text.</summary>
    public const string NotAvailableText = "This feature is not available yet.";

    private IItemCommandHandler[]? _handlers;

    /// <summary>Whether any handler implements the command for the item.</summary>
    public bool CanHandle(string command, BaseItemDto item) => Find(command, item) is not null;

    /// <summary>Runs the command with its handler, or tells the user that it is not available.</summary>
    public async Task<ItemCommandResult> ExecuteAsync(string command, BaseItemDto item, ItemActionContext? context = null)
    {
        var handler = Find(command, item);
        if (handler is null)
        {
            toast.Show(NotAvailableText);
            return ItemCommandResult.None;
        }

        return await handler.ExecuteAsync(command, item, context ?? new ItemActionContext());
    }

    private IItemCommandHandler? Find(string command, BaseItemDto item)
    {
        // Resolved late: handlers may themselves depend on the item services. The last registered wins.
        _handlers ??= services.GetServices<IItemCommandHandler>().Reverse().ToArray();
        return _handlers.FirstOrDefault(handler => handler.CanHandle(command, item));
    }
}
