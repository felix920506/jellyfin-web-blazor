using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.Session;

/// <summary>Helpers shared by the session, wizard and user settings page tests.</summary>
public static class PageTestExtensions
{
    /// <summary>Binds the API to the test server without signing in (login, wizard and password pages).</summary>
    public static void UseServer(this AppTestContext context) =>
        context.Services.GetRequiredService<JellyfinApi>().SetServer(TestApp.ServerUrl, TestApp.ServerId, "Test Server");

    /// <summary>Serves the given config.json (the repository has none) and loads it.</summary>
    public static Task UseConfigAsync(this AppTestContext context, object config)
    {
        context.App.Assets.OnGet("/config.json", config);
        return context.Services.GetRequiredService<WebConfig>().LoadAsync();
    }

    /// <summary>The open dialogs, rendered by a <see cref="DialogHost"/>.</summary>
    public static IRenderedComponent<DialogHost> RenderDialogs(this AppTestContext context) => context.Render<DialogHost>();

    public static AppDialogs Dialogs(this AppTestContext context) => context.Services.GetRequiredService<AppDialogs>();

    /// <summary>The texts of the toasts shown so far.</summary>
    public static IReadOnlyList<string> Toasts(this AppTestContext context) =>
        context.Services.GetRequiredService<ToastService>().Messages.Select(message => message.Text).ToList();

    /// <summary>How long <see cref="Eventually{TComponent}"/> and <see cref="Settle"/> wait at most.</summary>
    public static readonly TimeSpan PatienceLimit = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Waits until an assertion holds. Unlike <c>WaitForAssertion</c>, which only looks again when the component
    /// renders, this also keeps looking in between, so it can wait for requests, navigation and stored settings.
    /// </summary>
    public static void Eventually<TComponent>(this IRenderedComponent<TComponent> component, Action assertion, TimeSpan? timeout = null)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var limit = timeout ?? PatienceLimit;
        while (true)
        {
            try
            {
                component.WaitForAssertion(assertion, TimeSpan.FromMilliseconds(100));
                return;
            }
            catch (Bunit.Extensions.WaitForHelpers.WaitForFailedException) when (watch.Elapsed < limit)
            {
                // Look again.
            }
        }
    }

    /// <summary>
    /// Waits for an event handler to finish, or until it opens a dialog: a handler that asks the user something
    /// only finishes once the test has answered.
    /// </summary>
    public static async Task Settle(this AppTestContext context, Func<Task> trigger)
    {
        var dialogs = context.Dialogs();
        var open = dialogs.Dialogs.Count;
        // The handler runs up to its first wait (often the dialog) before this returns.
        var dispatch = trigger();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!dispatch.IsCompleted)
        {
            if (dialogs.Dialogs.Count > open)
            {
                return;
            }

            if (watch.Elapsed > PatienceLimit)
            {
                throw new TimeoutException("The event handler neither finished nor opened a dialog.");
            }

            await Task.Delay(5);
        }

        await dispatch;
    }

    public static bool HasClass(this AngleSharp.Dom.IElement element, string name) => element.ClassList.Contains(name);

    /// <summary>The English text of a translation key.</summary>
    public static string T(this AppTestContext context, string key) => context.Services.GetRequiredService<Globalize>()[key];

    public static bool IsLoading(this AppTestContext context) => context.Services.GetRequiredService<LoadingService>().IsVisible;

    /// <summary>The JSON a request was sent with.</summary>
    public static System.Text.Json.JsonElement JsonBody(this RecordedRequest request)
    {
        using var document = System.Text.Json.JsonDocument.Parse(request.Body ?? "null");
        return document.RootElement.Clone();
    }

    /// <summary>A string property of a JSON object; null when it is missing or null.</summary>
    public static string? Text(this System.Text.Json.JsonElement element, string name) =>
        element.ValueKind == System.Text.Json.JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A boolean property of a JSON object; null when it is missing or not a boolean.</summary>
    public static bool? Flag(this System.Text.Json.JsonElement element, string name) =>
        element.ValueKind == System.Text.Json.JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
            ? value.GetBoolean()
            : null;

    public static string? Value(this AngleSharp.Dom.IElement element) => element.GetAttribute("value");

    public static bool IsChecked(this AngleSharp.Dom.IElement element) => element.HasAttribute("checked");
}
