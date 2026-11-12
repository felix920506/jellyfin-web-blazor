using System.Globalization;

using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

/// <summary>One option of a version or track select.</summary>
public sealed record TrackOption(string Value, string Text);

/// <summary>One of the four selects of the track selection form.</summary>
public sealed record TrackSelect(IReadOnlyList<TrackOption> Options, string Value, bool Visible, bool Disabled)
{
    public static readonly TrackSelect Empty = new([], string.Empty, false, false);
}

/// <summary>
/// The track selection form of the details page (<c>renderTrackSelections</c> and its three
/// helpers): the versions of the item and, for the chosen version, its video, audio and subtitle
/// tracks with the original defaults. The values are what the selects hold, so what gets played is
/// derived from them exactly as the original reads its form.
/// </summary>
public sealed class TrackSelection
{
    /// <summary>The value of the "Off" subtitle option.</summary>
    public const string SubtitlesOff = "-1";

    private readonly IReadOnlyList<MediaSourceInfo> _sources;
    private readonly string _offText;

    private TrackSelection(IReadOnlyList<MediaSourceInfo> sources, string sourceId, string offText)
    {
        _sources = sources;
        _offText = offText;
        Source = new TrackSelect(
            sources.Select(source => new TrackOption(source.Id ?? string.Empty, source.Name ?? string.Empty)).ToList(),
            sourceId,
            Visible: sources.Count > 1,
            Disabled: false);
        LoadTracks();
    }

    public TrackSelect Source { get; private set; }

    public TrackSelect Video { get; private set; } = TrackSelect.Empty;

    public TrackSelect Audio { get; private set; } = TrackSelect.Empty;

    public TrackSelect Subtitles { get; private set; } = TrackSelect.Empty;

    /// <summary>
    /// The form for an item, or null when it has none: only videos with media sources that this
    /// client can play get one. <paramref name="currentSourceId"/> keeps the chosen version across
    /// a reload when the item still has it.
    /// </summary>
    public static TrackSelection? Create(BaseItemDto item, bool canPlay, string offText, string? currentSourceId = null)
    {
        // The original also asks the player for "PlayMediaSource", which every local player supports.
        if (item.MediaSources is not { Count: > 0 } sources || !DetailsRules.SupportsMediaSourceSelection(item) || !canPlay)
        {
            return null;
        }

        var sourceId = sources.Any(source => source.Id == currentSourceId) ? currentSourceId! : sources[0].Id ?? string.Empty;
        return new TrackSelection(sources, sourceId, offText);
    }

    /// <summary>Chooses a version; the track selects start over with that version's defaults.</summary>
    public void SelectSource(string? sourceId)
    {
        if (sourceId is null || sourceId == Source.Value || _sources.All(source => source.Id != sourceId))
        {
            return;
        }

        Source = Source with { Value = sourceId };
        LoadTracks();
    }

    public void SelectAudio(string? value)
    {
        if (value is not null && Audio.Options.Any(option => option.Value == value))
        {
            Audio = Audio with { Value = value };
        }
    }

    public void SelectSubtitles(string? value)
    {
        if (value is not null && Subtitles.Options.Any(option => option.Value == value))
        {
            Subtitles = Subtitles with { Value = value };
        }
    }

    /// <summary>What the form adds to a play request (<c>getPlayOptions</c>).</summary>
    public PlayOptions Apply(PlayOptions options) => options with
    {
        MediaSourceId = Source.Value.Length == 0 ? null : Source.Value,
        AudioStreamIndex = ParseIndex(Audio.Value),
        SubtitleStreamIndex = ParseIndex(Subtitles.Value)
    };

    /// <summary>The order of the tracks in a select (<c>itemHelper.sortTracks</c>).</summary>
    public static IReadOnlyList<MediaStream> SortTracks(IEnumerable<MediaStream> tracks) => tracks
        .OrderBy(track => track.IsExternal == true ? 1 : 0)
        .ThenBy(track => track.IsForced == true ? 0 : 1)
        .ThenBy(track => track.IsDefault == true ? 0 : 1)
        .ThenBy(track => track.Index ?? 0)
        .ToList();

    private static int? ParseIndex(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : null;

    private static string IndexText(int? index) => index?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private void LoadTracks()
    {
        var source = _sources.FirstOrDefault(candidate => candidate.Id == Source.Value);
        var streams = source?.MediaStreams ?? [];

        // Video: the first track is chosen and the select is for information only.
        var video = streams.Where(stream => stream.Type == MediaStream_Type.Video).ToList();
        Video = new TrackSelect(
            video.Select(stream => new TrackOption(IndexText(stream.Index), VideoTitle(stream))).ToList(),
            video.Count > 0 ? IndexText(video[0].Index) : string.Empty,
            Visible: video.Count > 0,
            Disabled: true);

        var audio = SortTracks(streams.Where(stream => stream.Type == MediaStream_Type.Audio));
        var audioOptions = audio.Select(stream => new TrackOption(IndexText(stream.Index), stream.DisplayTitle ?? string.Empty)).ToList();
        Audio = new TrackSelect(
            audioOptions,
            SelectedOrFirst(audioOptions, IndexText(source?.DefaultAudioStreamIndex)),
            Visible: audio.Count > 0,
            Disabled: audio.Count <= 1);

        var subtitles = SortTracks(streams.Where(stream => stream.Type == MediaStream_Type.Subtitle));
        if (subtitles.Count > 0)
        {
            var options = new List<TrackOption> { new(SubtitlesOff, _offText) };
            options.AddRange(subtitles.Select(stream => new TrackOption(IndexText(stream.Index), stream.DisplayTitle ?? string.Empty)));
            Subtitles = new TrackSelect(
                options,
                SelectedOrFirst(options, IndexText(source?.DefaultSubtitleStreamIndex ?? -1)),
                Visible: true,
                Disabled: false);
        }
        else
        {
            Subtitles = new TrackSelect([], string.Empty, Visible: false, Disabled: true);
        }
    }

    // A select without a matching option shows its first one, and that is what the form submits.
    private static string SelectedOrFirst(List<TrackOption> options, string value) =>
        options.Count == 0 ? string.Empty : options.Any(option => option.Value == value) ? value : options[0].Value;

    private static string VideoTitle(MediaStream stream)
    {
        if (!string.IsNullOrEmpty(stream.DisplayTitle))
        {
            return stream.DisplayTitle;
        }

        var parts = new List<string>();
        if (MediaInfo.GetResolutionText(stream.Width, stream.Height, stream.IsInterlaced == true) is { Length: > 0 } resolution)
        {
            parts.Add(resolution);
        }

        if (!string.IsNullOrEmpty(stream.Codec))
        {
            parts.Add(stream.Codec.ToUpperInvariant());
        }

        return string.Join(" ", parts);
    }
}
