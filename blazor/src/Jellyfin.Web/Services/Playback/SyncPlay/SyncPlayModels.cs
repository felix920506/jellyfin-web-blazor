using System.Text.Json.Serialization;

namespace Jellyfin.Web.Services;

/// <summary>One SyncPlay group returned by the server.</summary>
public sealed record SyncPlayGroup
{
    public string GroupId { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public IReadOnlyList<string> Participants { get; init; } = [];
}

/// <summary>The group state kept by the SyncPlay manager.</summary>
public sealed record SyncPlayGroupState
{
    public string GroupId { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public DateTimeOffset LastUpdatedAt { get; init; }
    public IReadOnlyList<string> Participants { get; init; } = [];
}

/// <summary>A queue entry from a SyncPlay PlayQueue update.</summary>
public sealed record SyncPlayQueueItem
{
    public string ItemId { get; init; } = string.Empty;
    public string PlaylistItemId { get; init; } = string.Empty;
}

/// <summary>The server's shared queue snapshot.</summary>
public sealed record SyncPlayQueueState
{
    public DateTimeOffset LastUpdate { get; init; }
    public IReadOnlyList<SyncPlayQueueItem> Playlist { get; init; } = [];
    public int PlayingItemIndex { get; init; } = -1;
    public long StartPositionTicks { get; init; }
    public string RepeatMode { get; init; } = "RepeatNone";
    public string ShuffleMode { get; init; } = "Sorted";

    public string? CurrentPlaylistItemId =>
        PlayingItemIndex >= 0 && PlayingItemIndex < Playlist.Count ? Playlist[PlayingItemIndex].PlaylistItemId : null;
}

/// <summary>A timestamped command sent by the SyncPlay server.</summary>
public sealed record SyncPlayCommand
{
    public string Command { get; init; } = string.Empty;
    public DateTimeOffset When { get; init; }
    public DateTimeOffset EmittedAt { get; init; }
    public long PositionTicks { get; init; }
    public string? PlaylistItemId { get; init; }
}

/// <summary>Server clock measurement used by the time estimator.</summary>
public sealed record SyncPlayTimeMeasurement(long RequestSent, long RequestReceived, long ResponseSent, long ResponseReceived)
{
    public long RoundTripMilliseconds => (ResponseReceived - RequestSent) - (ResponseSent - RequestReceived);

    /// <summary>Remote minus local time, using the minimum-latency sample.</summary>
    public double OffsetMilliseconds => ((RequestReceived - RequestSent) + (ResponseSent - ResponseReceived)) / 2d;

    public int PingMilliseconds => (int)Math.Round(RoundTripMilliseconds / 2d);
}

/// <summary>User-adjustable SyncPlay correction preferences.</summary>
public sealed record SyncPlaySettings
{
    public double ExtraTimeOffset { get; init; }
    public bool EnableSyncCorrection { get; init; }
    public double MinDelaySpeedToSync { get; init; } = 60;
    public double MaxDelaySpeedToSync { get; init; } = 3000;
    public double SpeedToSyncDuration { get; init; } = 1000;
    public double MinDelaySkipToSync { get; init; } = 400;
    public bool UseSpeedToSync { get; init; } = true;
    public bool UseSkipToSync { get; init; } = true;
}
