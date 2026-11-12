using Jellyfin.Sdk.Generated.Models;
using Microsoft.AspNetCore.Components;

namespace Jellyfin.Web.Services;

/// <summary>
/// The Live TV parts of the details page. The page decides when each of them applies (the rules
/// are in <see cref="DetailsRules"/>) and asks here for the part itself.
/// PORT-TODO(livetv): every member is an extension point for the Live TV workstream; until it
/// fills them in, the parts draw nothing and the buttons do nothing.
/// </summary>
[AppService]
public sealed class DetailsLiveTv
{
    /// <summary>
    /// The record button and the recording options of a programme
    /// (<c>components/recordingcreator/recordingfields</c>), inside <c>.recordingFields</c>.
    /// </summary>
    public RenderFragment? RecordingFields(BaseItemDto program) => null;

    /// <summary>
    /// The editor of a series timer embedded in its page
    /// (<c>components/recordingcreator/seriesrecordingeditor</c>), inside <c>.seriesRecordingEditor</c>.
    /// </summary>
    public RenderFragment? SeriesTimerEditor(BaseItemDto seriesTimer) => null;

    /// <summary>The upcoming recordings of a series timer (<c>renderSeriesTimerSchedule</c>), inside <c>#seriesTimerSchedule</c>.</summary>
    public RenderFragment? SeriesTimerSchedule(BaseItemDto seriesTimer) => null;

    /// <summary>The upcoming programmes of a channel (<c>renderChannelGuide</c>), inside <c>.programGuide</c>.</summary>
    public RenderFragment? ChannelGuide(BaseItemDto channel) => null;

    /// <summary>
    /// "Stop recording" (<c>recordingHelper.cancelTimer</c>). True when the timer was cancelled; the
    /// page then loads the item again.
    /// </summary>
    public Task<bool> CancelTimerAsync(BaseItemDto recording)
    {
        AppLog.Warn("Stopping a recording is not ported yet.");
        return Task.FromResult(false);
    }

    /// <summary>
    /// "Cancel series" (<c>recordingHelper.cancelSeriesTimerWithConfirmation</c>). True when the
    /// series timer was cancelled; the page then goes to Live TV.
    /// </summary>
    public Task<bool> CancelSeriesTimerAsync(BaseItemDto seriesTimer)
    {
        AppLog.Warn("Cancelling a series recording is not ported yet.");
        return Task.FromResult(false);
    }
}
