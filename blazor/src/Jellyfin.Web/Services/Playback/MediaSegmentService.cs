using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>Loads and decides which server media segment is actionable at the current position.</summary>
[AppService]
public sealed class MediaSegmentService(JellyfinApi api, UserSettings settings)
{
    private static readonly MediaSegmentType[] SegmentTypes =
    [MediaSegmentType.Intro, MediaSegmentType.Preview, MediaSegmentType.Recap, MediaSegmentType.Commercial, MediaSegmentType.Outro];

    public async Task<IReadOnlyList<PlaybackMediaSegment>> LoadAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        try
        {
            // The pinned SDK exposes the MediaSegments builder but its indexer is not usable from
            // the generated client surface in the WASM build. Keep the same server-backed route
            // through the app's JSON helper so this remains compatible with that SDK version.
            var query = string.Join('&', SegmentTypes.Select(type => $"IncludeSegmentTypes={Uri.EscapeDataString(type.ToString())}"));
            using var response = await api.SendAsync(
                HttpMethod.Get,
                $"MediaSegments/{itemId:N}?{query}",
                cancellationToken: cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = KiotaJson.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken),
                MediaSegmentDtoQueryResult.CreateFromDiscriminatorValue);
            return (result?.Items ?? [])
                .Where(segment => segment.StartTicks is >= 0 && segment.Type is not null)
                .Select(segment => new PlaybackMediaSegment(segment.Type!.Value.ToString(), segment.StartTicks!.Value, segment.EndTicks))
                .OrderBy(segment => segment.StartTicks)
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or Microsoft.Kiota.Abstractions.ApiException)
        {
            AppLog.Warn($"[playback] media segments could not be loaded: {ex.Message}");
            return [];
        }
    }

    public PlaybackMediaSegment? Current(IReadOnlyList<PlaybackMediaSegment> segments, long positionTicks) =>
        segments.FirstOrDefault(segment => segment.StartTicks <= positionTicks && (segment.EndTicks is null || segment.EndTicks > positionTicks) && Action(segment.Type) != "None");

    public string Action(string type) => settings.Get($"segmentTypeAction__{type}", false) ?? (type is "Intro" or "Outro" ? "AskToSkip" : "None");
}
