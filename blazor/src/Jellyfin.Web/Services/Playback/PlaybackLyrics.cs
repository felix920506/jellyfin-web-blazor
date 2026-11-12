using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services.Playback;

public sealed record PlaybackLyricLine(string Text, long? Start);

/// <summary>The original lyrics controller's tick-based timeline, also usable for remote targets.</summary>
[AppService]
public sealed class PlaybackLyrics(JellyfinApi api)
{
    public async Task<IReadOnlyList<PlaybackLyricLine>> LoadAsync(string itemId)
    {
        try
        {
            using var response = await api.SendAsync(HttpMethod.Get, $"Audio/{Uri.EscapeDataString(itemId)}/Lyrics");
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("Lyrics", out var lyrics) || lyrics.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return lyrics.EnumerateArray().Select(line => new PlaybackLyricLine(
                line.TryGetProperty("Text", out var text) ? text.GetString() ?? string.Empty : string.Empty,
                line.TryGetProperty("Start", out var start) && start.TryGetInt64(out var ticks) ? ticks : null)).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Warn($"[lyrics] loading failed: {ex.Message}");
            return [];
        }
    }

    // Like findLastIndex: duplicate timestamps select the last matching line. Unsynced lines do
    // not compare equal to time zero (they must never become clickable seek targets).
    public static int CurrentIndex(IReadOnlyList<PlaybackLyricLine> lines, long ticks)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i].Start is { } start && start <= ticks)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>Only DOM geometry and document-level input; timeline decisions stay in C#.</summary>
public interface IPlaybackLyricsUi
{
    Task AttachAsync<T>(ElementReference container, DotNetObjectReference<T> target) where T : class;
    Task ScrollAsync(ElementReference container, int index, bool smooth);
    Task DetachAsync(ElementReference container);
}

[AppService(typeof(IPlaybackLyricsUi))]
public sealed class JsPlaybackLyricsUi(IJSRuntime js) : IPlaybackLyricsUi
{
    private Task<IJSObjectReference>? _module;

    public Task AttachAsync<T>(ElementReference container, DotNetObjectReference<T> target) where T : class =>
        SendAsync("attach", container, target);

    public Task ScrollAsync(ElementReference container, int index, bool smooth) => SendAsync("scroll", container, index, smooth);

    public Task DetachAsync(ElementReference container) => SendAsync("detach", container);

    private async Task SendAsync(string method, params object?[] arguments)
    {
        try
        {
            var module = await (_module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/playback/lyrics.js").AsTask());
            await module.InvokeVoidAsync(method, arguments);
        }
        catch (Exception ex) when (ex is JSException or TaskCanceledException or InvalidOperationException)
        {
            AppLog.Warn($"[lyrics] {method}: {ex.Message}");
        }
    }
}
