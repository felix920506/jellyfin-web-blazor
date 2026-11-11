using System.Text.Json;
using Bunit.Rendering;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.Items;

/// <summary>
/// Base class of the item component tests: like <see cref="AppTestContext"/>, with a playback
/// manager the test controls (the app's default one can play nothing) and a fixed clock.
/// </summary>
public abstract class ItemsTestContext : BunitContext, IAsyncLifetime
{
    /// <summary>The time the item services see.</summary>
    public static readonly DateTimeOffset Now = new(2024, 5, 17, 12, 0, 0, TimeSpan.Zero);

    public TestApp App { get; } = new();

    public FakePlaybackManager Playback { get; } = new();

    public FakeItemsInterop Interop { get; } = new();

    public virtual async Task InitializeAsync()
    {
        App.Configure(Services);
        Services.AddSingleton<IPlaybackManager>(Playback);
        Services.AddSingleton<IItemsInterop>(Interop);
        ConfigureServices(Services);
        JSInterop.Mode = JSRuntimeMode.Loose;
        await App.LoadAssetsAsync(Services);
        Get<DateTimeText>().Clock = static () => Now;

        // The original waits ten seconds before reloading a list after a server event.
        Jellyfin.Web.Components.ItemsContainer.RefreshDelay = TimeSpan.FromMilliseconds(20);
    }

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    /// <summary>Extra registrations of a test class, added before the first service is created.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    /// <summary>The dialog that is open; fails when there is none or more than one.</summary>
    protected DialogInstance OpenDialog => Assert.Single(Get<AppDialogs>().Dialogs);

    /// <summary>Waits for a dialog to open (dialogs opened after an awaited request appear later).</summary>
    protected async Task<DialogInstance> NextDialogAsync()
    {
        var dialogs = Get<AppDialogs>();
        for (var attempt = 0; attempt < 300 && dialogs.Dialogs.Count == 0; attempt++)
        {
            await Task.Delay(10);
        }

        return OpenDialog;
    }

    /// <summary>The texts of the toasts shown so far.</summary>
    protected string[] Toasts => Get<ToastService>().Messages.Select(message => message.Text).ToArray();

    protected UserDto SignIn(bool admin = false, Action<UserDto>? configure = null) => App.SignIn(admin, Services, configure);

    protected T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    private readonly Dictionary<object, RenderFragment> _fragments = new(ReferenceEqualityComparer.Instance);

    /// <summary>Renders markup of a test, remembering it for <see cref="Rerender"/>.</summary>
    protected new IRenderedComponent<ContainerFragment> Render(RenderFragment fragment)
    {
        var cut = base.Render(fragment);
        _fragments[cut] = fragment;
        return cut;
    }

    /// <summary>
    /// Runs the markup of a test again, so that fields it reads (a location, options, items) reach the components
    /// as changed parameters. bUnit's container only renders when it is handed its content, so
    /// <c>cut.Render()</c> alone would change nothing.
    /// </summary>
    protected void Rerender(IRenderedComponent<ContainerFragment> cut)
    {
        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["ChildContent"] = _fragments[cut] });
        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(parameters)).GetAwaiter().GetResult();
    }

    /// <summary>Delivers a websocket message as the server would.</summary>
    protected void ServerMessage(string type, object data) =>
        Get<ServerEvents>().Dispatch(type, JsonSerializer.SerializeToElement(data));
}

/// <summary>Items as the server sends them.</summary>
public static class TestItems
{
    /// <summary>An item parsed from the JSON of an anonymous object: <c>TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie" })</c>.</summary>
    public static BaseItemDto Item(object value) =>
        KiotaJson.Parse(JsonSerializer.Serialize(value), BaseItemDto.CreateFromDiscriminatorValue)!;

    /// <summary>A stable item id in the server's format (no dashes).</summary>
    public static string Id(int number) => new Guid(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).ToString("N");

    public static object Movie(int number, string name, object? userData = null) => new
    {
        Id = Id(number),
        Name = name,
        ServerId = TestApp.ServerId,
        Type = "Movie",
        MediaType = "Video",
        IsFolder = false,
        ProductionYear = 2000 + number,
        RunTimeTicks = 72_000_000_000L,
        PrimaryImageAspectRatio = 0.6666666667,
        ImageTags = new { Primary = "tag" + number },
        UserData = userData ?? new { Played = false, IsFavorite = false, PlaybackPositionTicks = 0 }
    };
}

/// <summary>Stands in for <c>js/items.js</c>: counts image loads and lets a test report scroll states.</summary>
public sealed class FakeItemsInterop : IItemsInterop
{
    private Action<ScrollButtonState>? _onScrollState;

    public int ImageLoads { get; private set; }

    public int Watched { get; private set; }

    /// <summary>The scroll button presses: true for forward.</summary>
    public List<bool> Scrolls { get; } = [];

    public ValueTask LoadImagesAsync(Microsoft.AspNetCore.Components.ElementReference container)
    {
        ImageLoads++;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IAsyncDisposable> WatchScrollerAsync(Microsoft.AspNetCore.Components.ElementReference scroller, Action<ScrollButtonState> onChange)
    {
        _onScrollState = onChange;
        Watched++;
        return ValueTask.FromResult<IAsyncDisposable>(new Registration(this));
    }

    public ValueTask ScrollPageAsync(Microsoft.AspNetCore.Components.ElementReference scroller, bool forward)
    {
        Scrolls.Add(forward);
        return ValueTask.CompletedTask;
    }

    /// <summary>Reports a scroll state as the browser would.</summary>
    public void Report(bool visible, bool canScrollBack, bool canScrollForward) =>
        _onScrollState?.Invoke(new ScrollButtonState(visible, canScrollBack, canScrollForward));

    private sealed class Registration(FakeItemsInterop owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Watched--;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A playback manager that can play everything and records what it was asked to do.</summary>
public sealed class FakePlaybackManager : IPlaybackManager
{
    public event Action? StateChanged;

    public bool Playable { get; set; } = true;

    public bool Queueable { get; set; } = true;

    public bool IsPlaying { get; private set; }

    public bool IsPlayingVideo => IsPlaying && CurrentItem?.MediaType == BaseItemDto_MediaType.Video;

    public bool IsPlayingAudio => IsPlaying && CurrentItem?.MediaType == BaseItemDto_MediaType.Audio;

    public bool IsPaused => false;

    public BaseItemDto? CurrentItem { get; private set; }

    /// <summary>The calls made, e.g. "Play", "Queue", "Shuffle".</summary>
    public List<string> Calls { get; } = [];

    public List<PlayOptions> PlayRequests { get; } = [];

    public void Start(BaseItemDto item)
    {
        CurrentItem = item;
        IsPlaying = true;
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        IsPlaying = false;
        StateChanged?.Invoke();
        CurrentItem = null;
    }

    public bool CanPlay(BaseItemDto item) => Playable;

    public bool CanQueue(BaseItemDto item) => Queueable;

    public Task PlayAsync(PlayOptions options) => Record("Play", options);

    public Task QueueAsync(PlayOptions options) => Record("Queue", options);

    public Task QueueNextAsync(PlayOptions options) => Record("QueueNext", options);

    public Task ShuffleAsync(BaseItemDto item) => Record("Shuffle");

    public Task InstantMixAsync(BaseItemDto item) => Record("InstantMix");

    public Task PlayTrailersAsync(BaseItemDto item) => Record("PlayTrailers");

    public Task PauseAsync() => Record("Pause");

    public Task UnpauseAsync() => Record("Unpause");

    public Task PlayPauseAsync() => Record("PlayPause");

    public Task StopAsync() => Record("Stop");

    public Task NextTrackAsync() => Record("NextTrack");

    public Task PreviousTrackAsync() => Record("PreviousTrack");

    public Task SeekAsync(long positionTicks) => Record("Seek");

    private Task Record(string call, PlayOptions? options = null)
    {
        Calls.Add(call);
        if (options is not null)
        {
            PlayRequests.Add(options);
        }

        return Task.CompletedTask;
    }
}
