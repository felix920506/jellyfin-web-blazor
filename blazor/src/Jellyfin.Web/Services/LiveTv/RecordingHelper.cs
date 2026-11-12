using System.Text.Json;
using Jellyfin.Web.Components.Dialogs;

namespace Jellyfin.Web.Services;

/// <summary>
/// Scheduling and cancelling recordings (<c>components/recordingcreator/recordinghelper.js</c>):
/// the same requests, confirmations and toasts. Every method answers whether something changed
/// on the server; a failed request is logged and answers false.
/// </summary>
[AppService]
public sealed class RecordingHelper(LiveTvData data, ItemQueries items, AppDialogs dialogs, Globalize globalize, ToastService toast, LoadingService loading)
{
    /// <summary>Cancels a recording and says so (<c>cancelTimer</c>).</summary>
    public Task<bool> CancelTimerAsync(string timerId) => RunAsync(async () =>
    {
        await data.CancelTimerAsync(timerId);
        toast.Show(globalize["RecordingCancelled"]);
        return true;
    });

    /// <summary>Asks, then cancels a recording (<c>cancelTimerWithConfirmation</c>).</summary>
    public async Task<bool> CancelTimerWithConfirmationAsync(string timerId) =>
        await dialogs.ConfirmAsync(
            globalize["MessageConfirmRecordingCancellation"],
            confirmText: globalize["HeaderCancelRecording"],
            destructive: true,
            cancelText: globalize["HeaderKeepRecording"])
        && await CancelTimerAsync(timerId);

    /// <summary>Cancels a series recording and says so.</summary>
    /// <param name="seriesTimerId">The series timer.</param>
    /// <param name="message">The toast: "SeriesCancelled", or "RecordingCancelled" from the record button.</param>
    public Task<bool> CancelSeriesTimerAsync(string seriesTimerId, string message = "SeriesCancelled") => RunAsync(async () =>
    {
        await data.CancelSeriesTimerAsync(seriesTimerId);
        toast.Show(globalize[message]);
        return true;
    });

    /// <summary>Asks, then cancels a series recording (<c>cancelSeriesTimerWithConfirmation</c>).</summary>
    public async Task<bool> CancelSeriesTimerWithConfirmationAsync(string seriesTimerId) =>
        await dialogs.ConfirmAsync(
            globalize["MessageConfirmRecordingCancellation"],
            confirmText: globalize["HeaderCancelSeries"],
            destructive: true,
            cancelText: globalize["HeaderKeepSeries"])
        && await CancelSeriesTimerAsync(seriesTimerId);

    /// <summary>Schedules a programme, or its series, with the server's defaults (<c>createRecording</c>).</summary>
    public Task<bool> CreateRecordingAsync(string programId, bool isSeries = false) => RunAsync(async () =>
    {
        if (await data.GetTimerDefaultsAsync(programId) is not { } defaults)
        {
            return false;
        }

        if (isSeries)
        {
            await data.CreateSeriesTimerAsync(defaults);
        }
        else
        {
            await data.CreateTimerAsync(defaults);
        }

        toast.Show(globalize["RecordingScheduled"]);
        return true;
    });

    /// <summary>
    /// Turns the recording of an episode into one of its series; a programme that is no series
    /// has its recording cancelled instead (<c>changeRecordingToSeries</c>).
    /// </summary>
    public async Task<bool> ChangeRecordingToSeriesAsync(string timerId, string programId, bool confirmTimerCancellation = false)
    {
        var isSeries = false;
        var known = await RunAsync(async () =>
        {
            isSeries = (await items.GetItemAsync(programId))?.IsSeries == true;
            return true;
        });
        if (!known)
        {
            return false;
        }

        if (!isSeries)
        {
            return confirmTimerCancellation ? await CancelTimerWithConfirmationAsync(timerId) : await CancelTimerAsync(timerId);
        }

        return await RunAsync(async () =>
        {
            if (await data.GetTimerDefaultsAsync(programId) is not { } defaults)
            {
                return false;
            }

            await data.CreateSeriesTimerAsync(defaults);
            toast.Show(globalize["SeriesRecordingScheduled"]);
            return true;
        });
    }

    /// <summary>
    /// The record button of a card or list row (<c>toggleRecording</c>): a programme recorded as
    /// part of a series asks what to cancel, a single recording becomes a series recording (or is
    /// cancelled), anything else is scheduled.
    /// </summary>
    public async Task<bool> ToggleRecordingAsync(string? programId, string? timerId, string? timerStatus, string? seriesTimerId)
    {
        var hasTimer = !string.IsNullOrEmpty(timerId) && timerStatus != "Cancelled";
        if (!string.IsNullOrEmpty(seriesTimerId) && hasTimer)
        {
            return await ShowMultiCancellationPromptAsync(timerId!, timerStatus, seriesTimerId);
        }

        if (hasTimer && !string.IsNullOrEmpty(programId))
        {
            return await ChangeRecordingToSeriesAsync(timerId!, programId, true);
        }

        return !string.IsNullOrEmpty(programId) && await CreateRecordingAsync(programId);
    }

    private async Task<bool> ShowMultiCancellationPromptAsync(string timerId, string? timerStatus, string seriesTimerId)
    {
        var result = await dialogs.ShowAsync<MessageDialog>(
            new Dictionary<string, object?>
            {
                ["Text"] = globalize["MessageConfirmRecordingCancellation"],
                ["Buttons"] = new[]
                {
                    new DialogButton("cancel", globalize["HeaderKeepRecording"], "submit"),
                    new DialogButton("canceltimer", globalize[timerStatus == "InProgress" ? "HeaderStopRecording" : "HeaderCancelRecording"], "cancel"),
                    new DialogButton("cancelseriestimer", globalize["HeaderCancelSeries"], "cancel")
                }
            },
            new AppDialogOptions { Size = null });
        return (result as string) switch
        {
            "canceltimer" => await CancelTimerAsync(timerId),
            "cancelseriestimer" => await CancelSeriesTimerAsync(seriesTimerId),
            _ => false
        };
    }

    private async Task<bool> RunAsync(Func<Task<bool>> action)
    {
        loading.Show();
        try
        {
            return await action();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            AppLog.Warn($"Live TV: the recording request failed: {exception.Message}");
            return false;
        }
        finally
        {
            loading.Hide();
        }
    }
}
