using Jellyfin.Web.Components;

namespace Jellyfin.Web.Services;

/// <summary>
/// The dashboard's own dialogs, which look like the original's MUI dialogs (ConfirmDialog,
/// InputDialog, SimpleAlert) rather than the legacy dialogs of the user-facing pages.
/// </summary>
public static class DashboardDialogs
{
    /// <summary>Options for a dialog whose content draws its own MUI style title, content and actions.</summary>
    public static readonly AppDialogOptions Options = new() { Size = null, Class = "jf-dashboard-dialog" };

    /// <summary>A question with Cancel and a confirm button; true when confirmed.</summary>
    public static async Task<bool> MuiConfirmAsync(
        this AppDialogs dialogs, string title, string text, string? confirmText = null, bool destructive = false) =>
        await dialogs.ShowAsync<DashboardMessageDialog>(
            new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["Text"] = text,
                ["ConfirmText"] = confirmText,
                ["Destructive"] = destructive,
                ["ShowCancel"] = true,
            },
            Options) is true;

    /// <summary>A message with a "Got it" button.</summary>
    public static Task MuiAlertAsync(this AppDialogs dialogs, string? title, string text) =>
        dialogs.ShowAsync<DashboardMessageDialog>(
            new Dictionary<string, object?> { ["Title"] = title, ["Text"] = text },
            Options);

    /// <summary>Asks for a line of text; null when dismissed.</summary>
    public static async Task<string?> MuiInputAsync(
        this AppDialogs dialogs, string title, string label, string? helperText = null, string? confirmText = null, string? initialText = null) =>
        await dialogs.ShowAsync<DashboardMessageDialog>(
            new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["InputLabel"] = label,
                ["HelperText"] = helperText,
                ["ConfirmText"] = confirmText,
                ["InitialText"] = initialText,
            },
            Options) as string;
}
