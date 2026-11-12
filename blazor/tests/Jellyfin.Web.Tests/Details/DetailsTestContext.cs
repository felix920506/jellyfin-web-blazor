using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.Details;

/// <summary>
/// Base class of the details page tests: the item test context with stand-ins for the two
/// script modules of the page (<c>js/details.js</c>, <c>js/backdrop.js</c>).
/// </summary>
public abstract class DetailsTestContext : ItemsTestContext
{
    protected static readonly string UserIdText = TestApp.UserId.ToString("N");

    public FakeDetailsInterop Details { get; } = new();

    public FakeBackdropInterop BackdropDom { get; } = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IDetailsInterop>(Details);
        services.AddSingleton<IBackdropInterop>(BackdropDom);
    }
}

/// <summary>Stands in for <c>js/details.js</c>: the overview is wrapped in a paragraph, nothing is clamped unless a test says so.</summary>
public sealed class FakeDetailsInterop : IDetailsInterop
{
    public bool Clamped { get; set; }

    public int ImageLoads { get; private set; }

    /// <summary>The item ids "more from season" scrolled to.</summary>
    public List<string> ScrolledTo { get; } = [];

    public List<(string Url, string? Filename)> Downloads { get; } = [];

    public ValueTask<string?> RenderOverviewAsync(string markdown) =>
        ValueTask.FromResult<string?>("<em>" + System.Net.WebUtility.HtmlEncode(markdown) + "</em>");

    public ValueTask<bool> IsClampedAsync(ElementReference element) => ValueTask.FromResult(Clamped);

    public ValueTask ScrollToCardAsync(ElementReference section, string itemId)
    {
        ScrolledTo.Add(itemId);
        return ValueTask.CompletedTask;
    }

    public ValueTask LoadSiblingImagesAsync(ElementReference element)
    {
        ImageLoads++;
        return ValueTask.CompletedTask;
    }

    public ValueTask DownloadAsync(string url, string? filename)
    {
        Downloads.Add((url, filename));
        return ValueTask.CompletedTask;
    }
}

/// <summary>Stands in for <c>js/backdrop.js</c>: remembers the image that shows.</summary>
public sealed class FakeBackdropInterop : IBackdropInterop
{
    /// <summary>The image showing; null without a backdrop.</summary>
    public string? Url { get; private set; }

    /// <summary>Every image shown so far.</summary>
    public List<string> Shown { get; } = [];

    public int Clears { get; private set; }

    public ValueTask ShowAsync(string url)
    {
        Url = url;
        Shown.Add(url);
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync()
    {
        Url = null;
        Clears++;
        return ValueTask.CompletedTask;
    }
}
