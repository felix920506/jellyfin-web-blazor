using AngleSharp.Dom;
using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Session;
using Jellyfin.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.ItemDialogs;

/// <summary>
/// Base class of the item dialog tests: the dialogs are opened through their <c>ShowAsync</c> methods and
/// rendered by a <see cref="DialogHost"/>, the server is a <see cref="FakeHttp"/> and the browser side
/// (share, clipboard, downloads, file picker) a <see cref="FakeItemDialogsInterop"/>.
/// </summary>
public abstract class ItemDialogsTestContext : ItemsTestContext
{
    private IRenderedComponent<DialogHost>? _view;

    /// <summary>Stands in for <c>js/itemdialogs.js</c>.</summary>
    public FakeItemDialogsInterop Browser { get; } = new();

    /// <summary>Records where action sheets were asked to open.</summary>
    public FakeActionSheetPositioner Positioner { get; } = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IItemDialogsInterop>(Browser);
        services.AddSingleton<IActionSheetPositioner>(Positioner);
    }

    protected AppDialogs Dialogs => Get<AppDialogs>();

    protected FakeHttp Server => App.Server;

    /// <summary>The open dialogs as the page shows them.</summary>
    protected IRenderedComponent<DialogHost> View => _view ??= Render<DialogHost>();

    /// <summary>The English text of a translation key.</summary>
    protected string T(string key, params object?[] args) => Get<Globalize>().Translate(key, args);

    protected bool IsLoading => Get<LoadingService>().IsVisible;

    /// <summary>Opens a dialog and hands back the task that completes when it closes.</summary>
    protected Task<TResult> Open<TResult>(Func<Task<TResult>> show)
    {
        _ = View;
        return show();
    }

    /// <summary>Opens a dialog and hands back the task that completes when it closes.</summary>
    protected Task Open(Func<Task> show)
    {
        _ = View;
        return show();
    }

    /// <summary>Waits until an assertion about the page, the requests or the services holds.</summary>
    protected void Eventually(Action assertion) => View.Eventually(assertion);

    /// <summary>Waits for an event handler to finish, or until it opens a dialog that the test has to answer.</summary>
    protected async Task Settle(Func<Task> trigger)
    {
        var open = Dialogs.Dialogs.Count;
        var dispatch = trigger();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!dispatch.IsCompleted)
        {
            if (Dialogs.Dialogs.Count > open)
            {
                return;
            }

            if (watch.Elapsed > PageTestExtensions.PatienceLimit)
            {
                throw new TimeoutException("The event handler neither finished nor opened a dialog.");
            }

            await Task.Delay(5);
        }

        await dispatch;
    }

    protected IElement Find(string selector) => View.Find(selector);

    protected IReadOnlyList<IElement> FindAll(string selector) => View.FindAll(selector);

    protected Task Click(string selector) => Settle(() => Find(selector).ClickAsync(new MouseEventArgs()));

    protected Task Submit(string selector) => Settle(() => Find(selector).SubmitAsync());

    /// <summary>Types into a text input or text area.</summary>
    protected void Enter(string selector, string value) => Find(selector).Input(value);

    /// <summary>Picks an option of a select.</summary>
    protected void Pick(string selector, string value) => Find(selector).Change(value);

    /// <summary>Ticks or unticks a check box.</summary>
    protected void Check(string selector, bool value) => Find(selector).Change(value);

    /// <summary>Whether an element is hidden the way the original hides things (class "hide").</summary>
    protected bool IsHidden(string selector) => Find(selector).HasClass("hide");

    /// <summary>The values of the options of a select.</summary>
    protected string?[] Options(string selector) =>
        Find(selector).QuerySelectorAll("option").Select(option => option.GetAttribute("value")).ToArray();

    /// <summary>The title of the dialog on top.</summary>
    protected string DialogTitle => FindAll(".formDialogHeaderTitle")[^1].TextContent.Trim();

    /// <summary>The requests that changed something, as "METHOD /path".</summary>
    protected string[] Writes => Server.Requests
        .Where(request => request.Method != HttpMethod.Get)
        .Select(request => request.Method + " " + request.Path)
        .ToArray();

    /// <summary>Answers the confirmation or alert on top by its button ("ok" or "cancel").</summary>
    protected Task Answer(string button) => Click($"button.btnOption[data-id={button}]");

    /// <summary>Lets the server answer requests to a path with an error.</summary>
    protected void Fail(HttpMethod method, string pathSuffix, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.InternalServerError) =>
        Server.On(method, pathSuffix, _ => FakeHttp.Status(status));

    /// <summary>A file as the browser's file picker hands it over.</summary>
    protected static InputFileContent TextFile(string name, string text, string contentType = "text/plain") =>
        InputFileContent.CreateFromText(text, name, contentType: contentType);

    /// <summary>A binary file as the browser's file picker hands it over.</summary>
    protected static InputFileContent BinaryFile(string name, byte[] data, string contentType) =>
        InputFileContent.CreateFromBinary(data, name, contentType: contentType);
}

/// <summary>Stands in for <c>js/itemdialogs.js</c> and records what the dialogs asked the browser to do.</summary>
public sealed class FakeItemDialogsInterop : IItemDialogsInterop
{
    public bool CanShare { get; set; }

    /// <summary>What <see cref="CopyTextAsync"/> answers: false is a browser that refuses clipboard access.</summary>
    public bool CopyResult { get; set; } = true;

    public List<(string? Title, string? Text, string Url)> Shares { get; } = [];

    public List<string> Copies { get; } = [];

    /// <summary>One entry per download call: the URLs handed over together.</summary>
    public List<IReadOnlyList<string>> Downloads { get; } = [];

    public int Clicks { get; private set; }

    public ValueTask<bool> ShareAsync(string? title, string? text, string url)
    {
        Shares.Add((title, text, url));
        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> CopyTextAsync(string text)
    {
        Copies.Add(text);
        return ValueTask.FromResult(CopyResult);
    }

    public ValueTask DownloadAsync(IReadOnlyList<string> urls)
    {
        Downloads.Add(urls);
        return ValueTask.CompletedTask;
    }

    public ValueTask ClickAsync(ElementReference element)
    {
        Clicks++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Records the anchors action sheets were opened at.</summary>
public sealed class FakeActionSheetPositioner : IActionSheetPositioner
{
    public List<ActionSheetOptions> Calls { get; } = [];

    public ValueTask PositionAsync(ElementReference content, ActionSheetOptions options)
    {
        Calls.Add(options);
        return ValueTask.CompletedTask;
    }
}
