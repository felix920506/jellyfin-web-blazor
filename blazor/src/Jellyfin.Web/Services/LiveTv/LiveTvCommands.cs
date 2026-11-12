using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Components;

namespace Jellyfin.Web.Services;

/// <summary>
/// The Live TV commands of cards, list rows and the item menu (<c>shortcuts.js</c>,
/// <c>itemContextMenu.js</c>): the programme dialog, the record toggle, the recording editors and
/// cancelling timers.
/// </summary>
[AppService(typeof(IItemCommandHandler))]
public sealed class LiveTvCommandHandler(AppDialogs dialogs, RecordingHelper recordings) : IItemCommandHandler
{
    public bool CanHandle(string command, BaseItemDto item) => command switch
    {
        ItemCommand.Record or ItemCommand.ProgramDialog or ItemCommand.ToggleRecording
            or ItemCommand.CancelTimer or ItemCommand.CancelSeriesTimer => true,
        ItemCommand.Edit => item.TypeName() is "Timer" or "SeriesTimer",
        _ => false
    };

    public async Task<ItemCommandResult> ExecuteAsync(string command, BaseItemDto item, ItemActionContext context)
    {
        var type = item.TypeName();
        switch (command)
        {
            case ItemCommand.Record:
            case ItemCommand.ProgramDialog:
                return new ItemCommandResult(Updated: await RecordingCreator.ShowAsync(dialogs, item.IdText()));

            case ItemCommand.ToggleRecording:
                // onRecordCommand: only programmes and items with a timer have something to toggle.
                if (type != "Program" && string.IsNullOrEmpty(item.TimerId) && string.IsNullOrEmpty(item.SeriesTimerId))
                {
                    return ItemCommandResult.None;
                }

                return new ItemCommandResult(Updated: await recordings.ToggleRecordingAsync(
                    type == "Program" ? item.IdText() : null,
                    item.TimerId,
                    item.Status,
                    item.SeriesTimerId));

            case ItemCommand.CancelTimer:
                var timerId = string.IsNullOrEmpty(item.TimerId) ? item.IdText() : item.TimerId;
                return new ItemCommandResult(Deleted: await recordings.CancelTimerWithConfirmationAsync(timerId));

            case ItemCommand.CancelSeriesTimer:
                var seriesTimerId = string.IsNullOrEmpty(item.SeriesTimerId) ? item.IdText() : item.SeriesTimerId;
                return new ItemCommandResult(Deleted: await recordings.CancelSeriesTimerWithConfirmationAsync(seriesTimerId));

            case ItemCommand.Edit when type == "SeriesTimer" && !string.IsNullOrEmpty(item.SeriesTimerId):
                return await SeriesRecordingEditor.ShowAsync(dialogs, item.SeriesTimerId);

            case ItemCommand.Edit when type == "Timer":
                // editItem of shortcuts.js: a timer of a programme opens the programme dialog.
                if (!string.IsNullOrEmpty(item.ProgramId))
                {
                    return new ItemCommandResult(Updated: await RecordingCreator.ShowAsync(dialogs, item.ProgramId));
                }

                return string.IsNullOrEmpty(item.TimerId) ? ItemCommandResult.None : await RecordingEditor.ShowAsync(dialogs, item.TimerId);

            default:
                return ItemCommandResult.None;
        }
    }
}
