using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Items;

namespace Jellyfin.Web.Tests.Details;

/// <summary>The version and track selects of the details page and what they hand to playback.</summary>
public class TrackSelectionTests
{
    private static object Video(int index, string title) => new { Type = "Video", Index = index, DisplayTitle = title };

    private static object Audio(int index, string title, bool isDefault = false, bool external = false) =>
        new { Type = "Audio", Index = index, DisplayTitle = title, IsDefault = isDefault, IsExternal = external };

    private static object Subtitle(int index, string title, bool isDefault = false, bool forced = false, bool external = false) =>
        new { Type = "Subtitle", Index = index, DisplayTitle = title, IsDefault = isDefault, IsForced = forced, IsExternal = external };

    private static object Source(string id, string name, object[] streams, int? defaultAudio = null, int? defaultSubtitle = null) => new
    {
        Id = id,
        Name = name,
        Type = "Default",
        MediaStreams = streams,
        DefaultAudioStreamIndex = defaultAudio,
        DefaultSubtitleStreamIndex = defaultSubtitle
    };

    private static BaseItemDto Movie(params object[] sources) => TestItems.Item(new
    {
        Id = TestItems.Id(1),
        Name = "Movie",
        Type = "Movie",
        MediaType = "Video",
        MediaSources = sources
    });

    private static TrackSelection Create(BaseItemDto item, string? currentSourceId = null) =>
        TrackSelection.Create(item, canPlay: true, "Off", currentSourceId)!;

    [Fact]
    public void There_is_no_form_without_playable_versions()
    {
        var movie = Movie(Source("a", "Movie", [Video(0, "1080p H264")]));
        var song = TestItems.Item(new { Type = "Audio", MediaType = "Audio", MediaSources = new[] { Source("a", "Song", [Audio(0, "FLAC")]) } });

        Assert.NotNull(TrackSelection.Create(movie, canPlay: true, "Off"));
        Assert.Null(TrackSelection.Create(movie, canPlay: false, "Off"));
        Assert.Null(TrackSelection.Create(song, canPlay: true, "Off"));
        Assert.Null(TrackSelection.Create(TestItems.Item(new { Type = "Movie", MediaType = "Video" }), canPlay: true, "Off"));
    }

    [Fact]
    public void A_single_version_hides_the_version_select()
    {
        var selection = Create(Movie(Source("a", "Movie", [Video(0, "1080p H264"), Audio(1, "English - AAC - Stereo")])));

        Assert.False(selection.Source.Visible);
        Assert.Equal("a", selection.Source.Value);
        Assert.Equal([new TrackOption("a", "Movie")], selection.Source.Options);
    }

    [Fact]
    public void Several_versions_show_the_version_select_starting_with_the_first()
    {
        var selection = Create(Movie(
            Source("a", "1080p", [Video(0, "1080p H264")]),
            Source("b", "4K", [Video(0, "4K HEVC")])));

        Assert.True(selection.Source.Visible);
        Assert.False(selection.Source.Disabled);
        Assert.Equal("a", selection.Source.Value);
        Assert.Equal(["1080p", "4K"], selection.Source.Options.Select(option => option.Text));
    }

    [Fact]
    public void The_chosen_version_survives_a_reload_when_the_item_still_has_it()
    {
        var movie = Movie(Source("a", "1080p", [Video(0, "1080p H264")]), Source("b", "4K", [Video(0, "4K HEVC")]));

        Assert.Equal("b", Create(movie, "b").Source.Value);
        Assert.Equal("a", Create(movie, "gone").Source.Value);
    }

    [Fact]
    public void The_video_select_is_for_information_only()
    {
        var selection = Create(Movie(Source("a", "Movie", [Video(0, "1080p H264"), Audio(1, "English")])));

        Assert.True(selection.Video.Visible);
        Assert.True(selection.Video.Disabled);
        Assert.Equal("0", selection.Video.Value);
        Assert.Equal([new TrackOption("0", "1080p H264")], selection.Video.Options);
    }

    [Fact]
    public void A_video_track_without_title_is_named_by_resolution_and_codec()
    {
        var selection = Create(Movie(Source("a", "Movie", [new { Type = "Video", Index = 0, Width = 1920, Height = 1080, Codec = "h264" }])));

        Assert.Equal("1080p H264", Assert.Single(selection.Video.Options).Text);
    }

    [Fact]
    public void The_audio_select_starts_with_the_default_track_of_the_version()
    {
        var selection = Create(Movie(Source(
            "a",
            "Movie",
            [Video(0, "1080p"), Audio(1, "English - AAC"), Audio(2, "German - AC3", isDefault: true), Audio(3, "Commentary", external: true)],
            defaultAudio: 2)));

        Assert.True(selection.Audio.Visible);
        Assert.False(selection.Audio.Disabled);
        Assert.Equal("2", selection.Audio.Value);

        // Default tracks first, external ones last.
        Assert.Equal(["2", "1", "3"], selection.Audio.Options.Select(option => option.Value));
    }

    [Fact]
    public void A_single_audio_track_cannot_be_changed()
    {
        var selection = Create(Movie(Source("a", "Movie", [Video(0, "1080p"), Audio(1, "English - AAC")])));

        Assert.True(selection.Audio.Visible);
        Assert.True(selection.Audio.Disabled);
        Assert.Equal("1", selection.Audio.Value);
    }

    [Fact]
    public void Without_a_default_the_first_audio_track_is_chosen()
    {
        var selection = Create(Movie(Source("a", "Movie", [Video(0, "1080p"), Audio(1, "English"), Audio(2, "German")], defaultAudio: 9)));

        Assert.Equal("1", selection.Audio.Value);
    }

    [Fact]
    public void Subtitles_start_off_unless_the_version_has_a_default()
    {
        object[] streams = [Video(0, "1080p"), Audio(1, "English"), Subtitle(2, "English - SRT"), Subtitle(3, "Forced", forced: true), Subtitle(4, "External", external: true)];

        var off = Create(Movie(Source("a", "Movie", streams)));
        var chosen = Create(Movie(Source("a", "Movie", streams, defaultSubtitle: 3)));

        Assert.True(off.Subtitles.Visible);
        Assert.False(off.Subtitles.Disabled);
        Assert.Equal(TrackSelection.SubtitlesOff, off.Subtitles.Value);
        Assert.Equal(new TrackOption("-1", "Off"), off.Subtitles.Options[0]);

        // "Off", then forced tracks, the others and the external ones.
        Assert.Equal(["-1", "3", "2", "4"], off.Subtitles.Options.Select(option => option.Value));
        Assert.Equal("3", chosen.Subtitles.Value);
    }

    [Fact]
    public void Without_subtitle_tracks_the_select_is_hidden()
    {
        var selection = Create(Movie(Source("a", "Movie", [Video(0, "1080p"), Audio(1, "English")])));

        Assert.False(selection.Subtitles.Visible);
        Assert.True(selection.Subtitles.Disabled);
        Assert.Empty(selection.Subtitles.Options);
    }

    [Fact]
    public void Choosing_a_version_loads_its_tracks_with_their_defaults()
    {
        var selection = Create(Movie(
            Source("a", "1080p", [Video(0, "1080p"), Audio(1, "English"), Audio(2, "German"), Subtitle(3, "English")], defaultAudio: 1),
            Source("b", "4K", [Video(0, "4K"), Audio(1, "French"), Audio(2, "Spanish")], defaultAudio: 2)));
        selection.SelectAudio("2");
        selection.SelectSubtitles("3");

        selection.SelectSource("b");

        Assert.Equal("b", selection.Source.Value);
        Assert.Equal("4K", Assert.Single(selection.Video.Options).Text);
        Assert.Equal(["French", "Spanish"], selection.Audio.Options.Select(option => option.Text));
        Assert.Equal("2", selection.Audio.Value);
        Assert.False(selection.Subtitles.Visible);
    }

    [Fact]
    public void Unknown_values_are_ignored()
    {
        var selection = Create(Movie(Source("a", "Movie", [Video(0, "1080p"), Audio(1, "English"), Audio(2, "German"), Subtitle(3, "English")], defaultAudio: 1)));

        selection.SelectSource("nope");
        selection.SelectAudio("7");
        selection.SelectSubtitles(null);

        Assert.Equal("a", selection.Source.Value);
        Assert.Equal("1", selection.Audio.Value);
        Assert.Equal("-1", selection.Subtitles.Value);
    }

    [Fact]
    public void Playback_gets_the_version_and_the_chosen_tracks()
    {
        var selection = Create(Movie(
            Source("a", "1080p", [Video(0, "1080p"), Audio(1, "English"), Audio(2, "German"), Subtitle(3, "English"), Subtitle(4, "German")], defaultAudio: 1),
            Source("b", "4K", [Video(0, "4K"), Audio(1, "French")])));
        var request = new PlayOptions { StartPositionTicks = 50 };

        var defaults = selection.Apply(request);
        selection.SelectAudio("2");
        selection.SelectSubtitles("4");
        var chosen = selection.Apply(request);
        selection.SelectSource("b");
        var other = selection.Apply(request);

        Assert.Equal(("a", 1, -1), (defaults.MediaSourceId, defaults.AudioStreamIndex, defaults.SubtitleStreamIndex));
        Assert.Equal(50, defaults.StartPositionTicks);
        Assert.Equal(("a", 2, 4), (chosen.MediaSourceId, chosen.AudioStreamIndex, chosen.SubtitleStreamIndex));

        // A version without subtitle tracks sends no subtitle choice.
        Assert.Equal(("b", 1, null), (other.MediaSourceId, other.AudioStreamIndex, other.SubtitleStreamIndex));
    }

    [Fact]
    public void Tracks_sort_internal_before_external_then_forced_and_default()
    {
        MediaStream Stream(int index, bool external = false, bool forced = false, bool isDefault = false) =>
            new() { Index = index, IsExternal = external, IsForced = forced, IsDefault = isDefault };

        var sorted = TrackSelection.SortTracks([Stream(1), Stream(2, external: true, forced: true), Stream(3, isDefault: true), Stream(4, forced: true), Stream(0)]);

        Assert.Equal([4, 3, 0, 1, 2], sorted.Select(stream => stream.Index));
    }
}
