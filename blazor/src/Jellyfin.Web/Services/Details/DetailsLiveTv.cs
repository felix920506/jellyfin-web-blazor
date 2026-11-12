using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Components;
using Microsoft.AspNetCore.Components;

namespace Jellyfin.Web.Services;

/// <summary>
/// The Live TV parts of the details page. The page decides when each of them applies (the rules
/// are in <see cref="DetailsRules"/>) and asks here for the part itself. The page already wraps
/// the record buttons in <c>.recordingFields</c>, so the buttons are rendered without a second one.
/// </summary>
[AppService]
public sealed class DetailsLiveTv(LiveTvData data, RecordingHelper recordings)
{
    /// <summary>The list of a series timer's upcoming recordings (<c>getProgramScheduleHtml</c>).</summary>
    private static readonly ListViewOptions ScheduleList = new()
    {
        EnableUserDataButtons = false,
        Image = true,
        ImageSource = "channel",
        ShowProgramDateTime = true,
        ShowChannel = false,
        MediaInfo = true,
        Action = ItemAction.ProgramDialog,
        MoreButton = false,
        RecordButton = false
    };

    /// <summary>The list of one day of a channel's programmes (<c>renderProgramsForChannel</c>).</summary>
    private static readonly ListViewOptions ChannelList = new()
    {
        EnableUserDataButtons = false,
        ShowParentTitle = true,
        Image = false,
        ShowProgramTime = true,
        MediaInfo = false,
        ParentTitleWithTitle = true,
        MoreButton = false,
        RecordButton = false
    };

    /// <summary>
    /// The record button and the recording options of a programme
    /// (<c>components/recordingcreator/recordingfields</c>). The page supplies the <c>.recordingFields</c> wrapper.
    /// </summary>
    public RenderFragment RecordingFields(BaseItemDto program) => builder =>
    {
        builder.OpenComponent<RecordingFields>(0);
        builder.AddComponentParameter(1, nameof(Components.RecordingFields.ProgramId), program.IdText());
        builder.AddComponentParameter(2, nameof(Components.RecordingFields.Bare), true);
        builder.CloseComponent();
    };

    /// <summary>
    /// The editor of a series timer embedded in its page
    /// (<c>components/recordingcreator/seriesrecordingeditor</c>), inside <c>.seriesRecordingEditor</c>.
    /// The page has already loaded the timer, and the original renders that object rather than
    /// asking the server for it again.
    /// </summary>
    public RenderFragment SeriesTimerEditor(BaseItemDto seriesTimer) => builder =>
    {
        builder.OpenComponent<SeriesRecordingEditor>(0);
        builder.AddComponentParameter(1, nameof(Components.SeriesRecordingEditor.Timer), seriesTimer.Extras()?.SeriesTimer);
        builder.AddComponentParameter(2, nameof(Components.SeriesRecordingEditor.SeriesTimerId), seriesTimer.ActionId());
        builder.AddComponentParameter(3, nameof(Components.SeriesRecordingEditor.Embedded), true);
        builder.CloseComponent();
    };

    /// <summary>The upcoming recordings of a series timer (<c>renderSeriesTimerSchedule</c>), inside <c>#seriesTimerSchedule</c>.</summary>
    public RenderFragment SeriesTimerSchedule(BaseItemDto seriesTimer) => builder =>
    {
        builder.OpenComponent<ItemsContainer>(0);
        builder.AddComponentParameter(1, nameof(ItemsContainer.Class), "vertical-list");
        builder.AddComponentParameter(2, nameof(ItemsContainer.ListOptions), ScheduleList);
        builder.AddComponentParameter(3, nameof(ItemsContainer.Fetch), () => data.GetSeriesTimerScheduleAsync(seriesTimer.ActionId()));
        builder.CloseComponent();
    };

    /// <summary>The upcoming programmes of a channel (<c>renderChannelGuide</c>), inside <c>.programGuide</c>.</summary>
    public RenderFragment ChannelGuide(BaseItemDto channel) => builder =>
    {
        builder.OpenComponent<ChannelGuideView>(0);
        builder.AddComponentParameter(1, nameof(ChannelGuideView.ChannelId), channel.IdText());
        builder.CloseComponent();
    };

    /// <summary>
    /// "Stop recording" (<c>recordingHelper.cancelTimer</c>). True when the timer was cancelled; the
    /// page then loads the item again.
    /// </summary>
    public Task<bool> CancelTimerAsync(BaseItemDto recording) =>
        string.IsNullOrEmpty(recording.TimerId) ? Task.FromResult(false) : recordings.CancelTimerAsync(recording.TimerId);

    /// <summary>
    /// "Cancel series" (<c>recordingHelper.cancelSeriesTimerWithConfirmation</c>). True when the
    /// series timer was cancelled; the page then goes to Live TV.
    /// </summary>
    public Task<bool> CancelSeriesTimerAsync(BaseItemDto seriesTimer) =>
        recordings.CancelSeriesTimerWithConfirmationAsync(seriesTimer.ActionId());

    /// <summary>A channel's programmes that have not aired, one section per local day.</summary>
    private sealed class ChannelGuideView : ComponentBase
    {
        private IReadOnlyList<TimerGroup> _groups = [];

        [Inject]
        public LiveTvData Data { get; set; } = default!;

        [Inject]
        public DateTimeText Dates { get; set; } = default!;

        [Parameter, EditorRequired]
        public string ChannelId { get; set; } = string.Empty;

        protected override async Task OnParametersSetAsync()
        {
            try
            {
                _groups = LiveTvGrouping.GroupPrograms(await Data.GetChannelProgramsAsync(ChannelId), Dates);
            }
            catch (Exception exception) when (ItemDialogApi.IsFailure(exception))
            {
                AppLog.Warn($"Live TV: loading the guide of channel {ChannelId} failed: {exception.Message}");
            }
        }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            var sequence = 0;
            foreach (var group in _groups)
            {
                builder.OpenElement(sequence++, "div");
                builder.AddAttribute(sequence++, "class", "verticalSection verticalDetailSection");
                builder.OpenElement(sequence++, "h2");
                builder.AddAttribute(sequence++, "class", "sectionTitle padded-left");
                builder.AddContent(sequence++, group.Name);
                builder.CloseElement();
                builder.OpenComponent<ItemsContainer>(sequence++);
                builder.AddComponentParameter(sequence++, nameof(ItemsContainer.Class), "vertical-list padded-left padded-right");
                builder.AddComponentParameter(sequence++, nameof(ItemsContainer.Items), group.Items);
                builder.AddComponentParameter(sequence++, nameof(ItemsContainer.ListOptions), ChannelList);
                builder.CloseComponent();
                builder.CloseElement();
            }
        }
    }
}
