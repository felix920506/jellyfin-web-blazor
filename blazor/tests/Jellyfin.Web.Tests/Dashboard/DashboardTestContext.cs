using System.Text.Json;
using Jellyfin.Web.Components;
using Jellyfin.Web.Routing;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Session;
using Jellyfin.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Jellyfin.Web.Tests.Dashboard;

/// <summary>
/// Base class of the dashboard tests: an administrator's app with a fixed clock, the dashboard's
/// script module in loose mode (relative times fall back to their English texts) and MudBlazor
/// popovers that work without a provider in the test's render tree.
/// </summary>
public abstract class DashboardTestContext : AppTestContext
{
    /// <summary>What the app's clock shows: a Wednesday noon.</summary>
    public static readonly DateTimeOffset Now = new(2024, 5, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The calls into js/dashboard.js.</summary>
    protected BunitJSModuleInterop Module { get; private set; } = default!;

    public override async Task InitializeAsync()
    {
        Services.PostConfigure<PopoverOptions>(options => options.CheckForPopoverProvider = false);
        await base.InitializeAsync();

        // MudBlazor's popovers ask where they are rendered when they are disposed.
        Renderer.SetRendererInfo(new RendererInfo("WebAssembly", true));
        Module = JSInterop.SetupModule("./js/dashboard.js");
        Module.Mode = JSRuntimeMode.Loose;
        Get<DateTimeText>().Clock = static () => Now;
    }

    protected T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Delivers a web socket message to the app.</summary>
    protected void Push(string messageType, object data) =>
        Get<ServerEvents>().Dispatch(messageType, JsonSerializer.SerializeToElement(data, new JsonSerializerOptions { PropertyNamingPolicy = null }));

    /// <summary>Renders a page as the router does: with the location it is shown at.</summary>
    protected IRenderedComponent<TPage> RenderAt<TPage>(string url, Action<ComponentParameterCollectionBuilder<TPage>>? parameters = null)
        where TPage : IComponent
    {
        var location = AppLocation.Parse(url);
        var host = Render<CascadingValue<AppLocation>>(cascade => cascade
            .Add(c => c.Value, location)
            .AddChildContent<TPage>(parameters));
        return host.FindComponent<TPage>();
    }

    /// <summary>Clicks something that asks a question and waits for the dashboard's dialog to show.</summary>
    protected async Task ClickForDialogAsync(IRenderedComponent<DialogHost> dialogs, AngleSharp.Dom.IElement element)
    {
        await this.Settle(() => element.ClickAsync(new MouseEventArgs()));
        dialogs.Eventually(() => Assert.NotNull(dialogs.Find("form.jf-dashboard-dialog-form")));
    }

    /// <summary>Presses the confirm button of the dashboard's dialog, after typing into its input if it has one.</summary>
    protected static async Task ConfirmDialogAsync(IRenderedComponent<DialogHost> dialogs, string? text = null)
    {
        if (text is not null)
        {
            await dialogs.Find("#jf-dashboard-dialog-input").InputAsync(new ChangeEventArgs { Value = text });
        }

        await dialogs.Find("form.jf-dashboard-dialog-form").SubmitAsync();
    }

    /// <summary>Presses Cancel in the dashboard's dialog.</summary>
    protected static Task CancelDialogAsync(IRenderedComponent<DialogHost> dialogs) =>
        dialogs.Find("form.jf-dashboard-dialog-form [data-id=cancel]").ClickAsync(new MouseEventArgs());

    /// <summary>The checkbox input of a MudBlazor checkbox or switch with the given class.</summary>
    protected static AngleSharp.Dom.IElement Box<TComponent>(IRenderedComponent<TComponent> cut, string cssClass)
        where TComponent : IComponent => cut.Find($".{cssClass} input[type=checkbox], input.{cssClass}[type=checkbox]");

    /// <summary>The MudBlazor select with the given class.</summary>
    protected static MudSelect<TValue> Select<TComponent, TValue>(IRenderedComponent<TComponent> cut, string cssClass)
        where TComponent : class, IComponent =>
        cut.FindComponents<MudSelect<TValue>>().Single(select => (select.Instance.Class ?? string.Empty).Split(' ').Contains(cssClass)).Instance;

    /// <summary>The value a MudBlazor select shows.</summary>
    protected static TValue? Chosen<TComponent, TValue>(IRenderedComponent<TComponent> cut, string cssClass)
        where TComponent : class, IComponent
    {
#pragma warning disable MUD0012 // Reading the parameter is what a test of the rendered state wants.
        return Select<TComponent, TValue>(cut, cssClass).Value;
#pragma warning restore MUD0012
    }

    /// <summary>Picks a value in a MudBlazor select, as choosing it from the opened list does.</summary>
    protected static Task ChooseAsync<TComponent, TValue>(IRenderedComponent<TComponent> cut, string cssClass, TValue value)
        where TComponent : class, IComponent
    {
        var select = Select<TComponent, TValue>(cut, cssClass);
        return cut.InvokeAsync(() => select.ValueChanged.InvokeAsync(value));
    }

    /// <summary>A plain text answer of the server.</summary>
    protected static HttpResponseMessage PlainText(string text) =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(text, System.Text.Encoding.UTF8, "text/plain") };

    /// <summary>The JSON bodies of the requests sent to a path so far.</summary>
    protected IReadOnlyList<JsonElement> BodiesTo(string pathSuffix, HttpMethod? method = null) =>
        App.Server.RequestsTo(pathSuffix)
            .Where(request => request.Method == (method ?? HttpMethod.Post))
            .Select(request =>
            {
                using var document = JsonDocument.Parse(request.Body ?? "null");
                return document.RootElement.Clone();
            })
            .ToList();
}
