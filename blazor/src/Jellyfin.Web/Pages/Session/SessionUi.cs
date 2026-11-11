using System.Globalization;
using System.Net;
using Jellyfin.Web.Components.Dialogs;
using Jellyfin.Web.Services;

namespace Jellyfin.Web.Pages.Session;

/// <summary>Small helpers shared by the session, wizard and user settings pages.</summary>
public static class SessionUi
{
    /// <summary>
    /// Port of cardbuilder/utils/builder.ts getDefaultColorIndex: a colour 1..5 derived from the
    /// text, or a random one without text.
    /// </summary>
    public static int DefaultColorIndex(string? text = null)
    {
        const int NumberOfColors = 5;
        if (string.IsNullOrEmpty(text))
        {
            return Random.Shared.Next(1, NumberOfColors + 1);
        }

        var character = ((int)text[text.Length / 2]).ToString(CultureInfo.InvariantCulture);
        var sum = 0;
        foreach (var digit in character)
        {
            sum += digit - '0';
        }

        var index = sum % 10;
        return (index % NumberOfColors) + 1;
    }

    /// <summary>Port of getDefaultBackgroundClass.</summary>
    public static string DefaultBackgroundClass(string? text = null) =>
        "defaultCardBackground defaultCardBackground" + DefaultColorIndex(text).ToString(CultureInfo.InvariantCulture);

    /// <summary>Port of utils/url.ts isValidUrl: an absolute http(s) URL.</summary>
    public static bool IsValidUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    public static string HtmlEncode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>
    /// An alert whose text is markup, as the original's alert renders its text as (sanitised)
    /// HTML. <paramref name="html"/> must be trusted: encode server or user supplied parts.
    /// </summary>
    public static Task AlertHtmlAsync(this AppDialogs dialogs, Globalize globalize, string html, string? title = null) =>
        dialogs.ShowAsync<MessageDialog>(
            new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["Html"] = html,
                ["Buttons"] = new[] { new DialogButton("ok", globalize["ButtonGotIt"], "submit") }
            },
            new AppDialogOptions { Size = null });

    /// <summary>
    /// A confirmation whose text is markup (the original's confirm renders its text as HTML).
    /// <paramref name="html"/> must be trusted: encode server or user supplied parts.
    /// </summary>
    public static async Task<bool> ConfirmHtmlAsync(this AppDialogs dialogs, Globalize globalize, string html, string? title = null, string? confirmText = null, bool destructive = false)
    {
        var result = await dialogs.ShowAsync<MessageDialog>(
            new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["Html"] = html,
                ["Buttons"] = new[]
                {
                    new DialogButton("cancel", globalize["ButtonCancel"], "cancel"),
                    new DialogButton("ok", confirmText ?? globalize["ButtonOk"], destructive ? "delete" : "submit")
                }
            },
            new AppDialogOptions { Size = null });
        return result as string == "ok";
    }

    /// <summary>Port of Dashboard.alert({ message, title }): the title defaults to "Alert".</summary>
    public static Task DashboardAlertAsync(this AppDialogs dialogs, Globalize globalize, string message, string? title = null) =>
        dialogs.AlertAsync(message, title ?? globalize["HeaderAlert"]);
}
