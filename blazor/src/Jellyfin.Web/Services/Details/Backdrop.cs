using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;
using Jellyfin.Web.Services.Playback;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

/// <summary>The DOM side of the page backdrop (<c>wwwroot/js/backdrop.js</c>).</summary>
public interface IBackdropInterop
{
    /// <summary>Shows an image behind the page, fading it in over the one showing.</summary>
    ValueTask ShowAsync(string url);

    /// <summary>Removes the image.</summary>
    ValueTask ClearAsync();
}

/// <summary><see cref="IBackdropInterop"/> over the <c>js/backdrop.js</c> module, loaded on first use.</summary>
[AppService(typeof(IBackdropInterop))]
public sealed class BackdropInterop(IJSRuntime js) : IBackdropInterop
{
    private Task<IJSObjectReference?>? _module;

    public async ValueTask ShowAsync(string url)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("show", url);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Showing the backdrop failed: " + exception.Message);
        }
    }

    public async ValueTask ClearAsync()
    {
        // Nothing was ever shown: there is nothing to clear, and no reason to load the module.
        if (_module is null)
        {
            return;
        }

        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("clear");
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Clearing the backdrop failed: " + exception.Message);
        }
    }

    private static bool IsInteropFailure(Exception exception) =>
        exception is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;

    private Task<IJSObjectReference?> GetModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync() =>
        await js.InvokeAsync<IJSObjectReference?>("import", "./js/backdrop.js");
}

/// <summary>
/// The backdrop behind a page: the backdrop images of an item (or of several), shown one after
/// the other. Port of <c>components/backdrop/backdrop.js</c>. Any page may use it: call
/// <see cref="SetBackdrops"/> when the page shows and <see cref="Clear"/> when it goes away.
/// The user's "backdrops" display setting decides whether anything shows, unless the page says
/// its backdrop is part of its design (the banner of the details page).
/// </summary>
[AppService]
public sealed class Backdrop(IBackdropInterop interop, UserSettings settings, JellyfinApi api, IBrowser browser, IPlaybackManager playback)
{
    private IReadOnlyList<string> _images = [];
    private CancellationTokenSource? _rotation;
    private int _index = -1;

    /// <summary>How long an image shows before the next one of several takes over.</summary>
    internal static TimeSpan RotationInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The images in rotation; empty without a backdrop.</summary>
    public IReadOnlyList<string> Images => _images;

    /// <summary>
    /// Shows the backdrop images of the items, or removes the backdrop when they have none.
    /// Without <paramref name="enabled"/> this does nothing unless the user turned backdrops on.
    /// </summary>
    public void SetBackdrops(IReadOnlyList<BaseItemDto> items, bool enabled = false)
    {
        if (!enabled && !settings.EnableBackdrops)
        {
            return;
        }

        var images = items.SelectMany(GetImageUrls).ToList();
        if (images.Count == 0)
        {
            Clear();
            return;
        }

        if (images.SequenceEqual(_images))
        {
            return;
        }

        StopRotation();
        _images = images;
        _index = -1;
        ShowNext();
        if (images.Count > 1)
        {
            StartRotation();
        }
    }

    /// <summary>Removes the backdrop.</summary>
    public void Clear()
    {
        StopRotation();
        _images = [];
        _index = -1;
        _ = interop.ClearAsync();
    }

    /// <summary>
    /// The backdrop image addresses of an item, sized for the screen (<c>getImageUrls</c>): its own
    /// backdrops, else those of the parent it inherits them from.
    /// </summary>
    public IReadOnlyList<string> GetImageUrls(BaseItemDto item)
    {
        if (item.BackdropImageTags is { Count: > 0 } tags)
        {
            return tags.Select((tag, index) => GetBackdropUrl(item.IdText(), tag, index)).ToList();
        }

        if (item.ParentBackdropItemId is { } parentId && item.ParentBackdropImageTags is { Count: > 0 } parentTags)
        {
            return parentTags.Select((tag, index) => GetBackdropUrl(parentId.ToString("N"), tag, index)).ToList();
        }

        return [];
    }

    /// <summary>The address of one backdrop image of an item, at most as wide as the screen.</summary>
    public string GetBackdropUrl(string itemId, string? tag, int index) => GetImageUrl(itemId, "Backdrop", tag, index);

    /// <summary>
    /// The address of an image of an item at most as wide as the screen (<c>getScaledImageUrl</c>
    /// with <c>maxWidth: screenWidth</c>).
    /// </summary>
    public string GetImageUrl(string itemId, string imageType, string? tag, int? index = null)
    {
        var environment = browser.Environment;
        var maxWidth = (int)Math.Round(environment.ScreenWidth * environment.DevicePixelRatio, MidpointRounding.AwayFromZero);
        var path = index is { } number
            ? $"Items/{itemId}/Images/{imageType}/{DetailsRules.Invariant(number)}"
            : $"Items/{itemId}/Images/{imageType}";
        return api.GetUrl(path, ("maxWidth", maxWidth), ("tag", tag), ("quality", imageType == "Backdrop" ? 80 : 90));
    }

    private void ShowNext()
    {
        if (_images.Count == 0)
        {
            return;
        }

        _index = (_index + 1) % _images.Count;
        _ = interop.ShowAsync(_images[_index]);
    }

    private void StartRotation()
    {
        var rotation = _rotation = new CancellationTokenSource();
        _ = RotateAsync(rotation.Token);
    }

    private async Task RotateAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(RotationInterval, cancellationToken);

                // The image stays while a video plays in front of it.
                if (!cancellationToken.IsCancellationRequested && !playback.IsPlayingVideo)
                {
                    ShowNext();
                }
            }
        }
        catch (TaskCanceledException)
        {
            // Stopped: another backdrop took over or the page went away.
        }
    }

    private void StopRotation()
    {
        if (_rotation is { } rotation)
        {
            _rotation = null;
            rotation.Cancel();
            rotation.Dispose();
        }
    }
}
