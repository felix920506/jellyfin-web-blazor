using Microsoft.AspNetCore.Components;

namespace Jellyfin.Web.Services;

/// <summary>The global busy spinner. Port of components/loading/loading.ts.</summary>
public sealed class LoadingService
{
    public event Action? Changed;

    public bool IsVisible { get; private set; }

    public void Show() => Set(true);

    public void Hide() => Set(false);

    private void Set(bool visible)
    {
        if (IsVisible != visible)
        {
            IsVisible = visible;
            Changed?.Invoke();
        }
    }
}

/// <summary>Short messages at the bottom of the screen. Port of components/toast/toast.ts.</summary>
public sealed class ToastService
{
    private readonly List<ToastMessage> _messages = [];

    public event Action? Changed;

    public IReadOnlyList<ToastMessage> Messages => _messages;

    public void Show(string text)
    {
        var message = new ToastMessage(text);
        _messages.Add(message);
        Changed?.Invoke();
        _ = ExpireAsync(message);
    }

    private async Task ExpireAsync(ToastMessage message)
    {
        await Task.Delay(300);
        message.Visible = true;
        Changed?.Invoke();
        await Task.Delay(3300);
        message.Visible = false;
        message.Hiding = true;
        Changed?.Invoke();
        await Task.Delay(300);
        _messages.Remove(message);
        Changed?.Invoke();
    }
}

public sealed class ToastMessage(string text)
{
    public string Text { get; } = text;
    public bool Visible { get; set; }
    public bool Hiding { get; set; }
}

/// <summary>One entry of an action sheet (popup menu).</summary>
public sealed record ActionSheetItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public bool Selected { get; init; }
    public string? SecondaryText { get; init; }
    public string? AsideText { get; init; }

    /// <summary>Renders a divider instead of an item.</summary>
    public bool Divider { get; init; }
}

public sealed record ActionSheetOptions
{
    public IReadOnlyList<ActionSheetItem> Items { get; init; } = [];
    public string? Title { get; init; }
    public string? Text { get; init; }
    public bool ShowCancel { get; init; }
}

/// <summary>Options for a dialog opened with <see cref="AppDialogs.ShowAsync{TComponent}"/>.</summary>
public sealed record AppDialogOptions
{
    /// <summary>"small", "medium", "medium-tall", "fullscreen", "fullscreen-border" or null to fit the content.</summary>
    public string? Size { get; init; } = "small";

    /// <summary>Extra CSS classes on the <c>.dialog</c> element, e.g. "formDialog".</summary>
    public string? Class { get; init; } = "formDialog";

    /// <summary>Close when the backdrop is clicked or Escape is pressed.</summary>
    public bool Dismissable { get; init; } = true;
}

/// <summary>An open dialog. Cascaded to the dialog's content component.</summary>
public sealed class DialogInstance
{
    private readonly TaskCompletionSource<object?> _closed = new();
    private readonly AppDialogs _owner;

    internal DialogInstance(AppDialogs owner, RenderFragment content, AppDialogOptions options)
    {
        _owner = owner;
        Content = content;
        Options = options;
    }

    public RenderFragment Content { get; }

    public AppDialogOptions Options { get; }

    internal Task<object?> Result => _closed.Task;

    /// <summary>Closes the dialog; the awaiting caller receives <paramref name="result"/>.</summary>
    public void Close(object? result = null)
    {
        if (_closed.TrySetResult(result))
        {
            _owner.Remove(this);
        }
    }

    /// <summary>Closes the dialog without a result (same as dismissing it).</summary>
    public void Cancel() => Close(null);
}

/// <summary>
/// Modal dialogs with the original client's markup (dialogHelper, alert, confirm, prompt,
/// actionSheet). Rendered by <c>DialogHost</c>, which lives in the root component.
/// </summary>
public sealed class AppDialogs(Globalize globalize)
{
    private readonly List<DialogInstance> _dialogs = [];

    public event Action? Changed;

    public IReadOnlyList<DialogInstance> Dialogs => _dialogs;

    /// <summary>
    /// Opens a component as a dialog and waits until it closes. The component receives a
    /// <c>[CascadingParameter] DialogInstance</c> and calls <c>Close(result)</c>; the result is null
    /// when the dialog was dismissed.
    /// </summary>
    public Task<object?> ShowAsync<TComponent>(IDictionary<string, object?>? parameters = null, AppDialogOptions? options = null)
        where TComponent : IComponent
    {
        RenderFragment content = builder =>
        {
            builder.OpenComponent<TComponent>(0);
            if (parameters is not null)
            {
                builder.AddMultipleAttributes(1, parameters!);
            }

            builder.CloseComponent();
        };

        return ShowAsync(content, options);
    }

    /// <summary>Opens arbitrary content as a dialog.</summary>
    public Task<object?> ShowAsync(RenderFragment content, AppDialogOptions? options = null)
    {
        var dialog = new DialogInstance(this, content, options ?? new AppDialogOptions());
        _dialogs.Add(dialog);
        Changed?.Invoke();
        return dialog.Result;
    }

    /// <summary>A message with a single "Got it" button.</summary>
    public Task AlertAsync(string text, string? title = null) => ShowAsync<Components.Dialogs.MessageDialog>(
        new Dictionary<string, object?>
        {
            ["Title"] = title,
            ["Text"] = text,
            ["Buttons"] = new[] { new DialogButton("ok", globalize["ButtonGotIt"], "submit") }
        },
        new AppDialogOptions { Size = null });

    /// <summary>A question with Cancel and OK buttons; true when OK was chosen.</summary>
    public async Task<bool> ConfirmAsync(string text, string? title = null, string? confirmText = null, bool destructive = false, string? cancelText = null)
    {
        var result = await ShowAsync<Components.Dialogs.MessageDialog>(
            new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["Text"] = text,
                ["Buttons"] = new[]
                {
                    new DialogButton("cancel", cancelText ?? globalize["ButtonCancel"], "cancel"),
                    new DialogButton("ok", confirmText ?? globalize["ButtonOk"], destructive ? "delete" : "submit")
                }
            },
            new AppDialogOptions { Size = null });
        return result as string == "ok";
    }

    /// <summary>Asks for a line of text; null when cancelled.</summary>
    public async Task<string?> PromptAsync(string? title, string? label = null, string? value = null, string? description = null, string? confirmText = null)
    {
        var result = await ShowAsync<Components.Dialogs.PromptDialog>(
            new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["Label"] = label,
                ["Value"] = value,
                ["Description"] = description,
                ["ConfirmText"] = confirmText
            },
            new AppDialogOptions { Size = "small" });
        return result as string;
    }

    /// <summary>Shows a menu of choices; returns the chosen item's id, or null when dismissed.</summary>
    public async Task<string?> ActionSheetAsync(ActionSheetOptions options)
    {
        var result = await ShowAsync<Components.Dialogs.ActionSheetDialog>(
            new Dictionary<string, object?> { ["Options"] = options },
            new AppDialogOptions { Size = null, Class = "actionSheet actionsheet-not-fullscreen" });
        return result as string;
    }

    /// <summary>Closes every open dialog (used on navigation).</summary>
    public void CloseAll()
    {
        foreach (var dialog in _dialogs.ToArray())
        {
            dialog.Cancel();
        }
    }

    internal void Remove(DialogInstance dialog)
    {
        if (_dialogs.Remove(dialog))
        {
            Changed?.Invoke();
        }
    }
}

/// <summary>A button of a message dialog. Type is "submit", "cancel" or "delete".</summary>
public sealed record DialogButton(string Id, string Name, string Type);
