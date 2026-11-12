using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

/// <summary>What <c>getPosition</c> of <c>actionSheet.ts</c> measures: the anchor's box, the sheet and the window.</summary>
public readonly record struct ActionSheetMeasure(
    double Top,
    double Left,
    double Width,
    double Height,
    double DialogWidth,
    double DialogHeight,
    double WindowWidth,
    double WindowHeight);

/// <summary>The position of an anchored action sheet (<c>getPosition</c> of <c>actionSheet.ts</c>).</summary>
public static class ActionSheetPosition
{
    /// <summary>
    /// Left and top of the sheet in window coordinates: centred on the anchor (on its top edge with
    /// <c>positionY: 'top'</c>), moved back inside the window when it would leave it at the right or the
    /// bottom, shifted by the offsets and never closer than ten pixels to the left and top edges.
    /// </summary>
    public static (double Left, double Top) Compute(ActionSheetMeasure measure, string? positionY = null, double offsetLeft = 0, double offsetTop = 0)
    {
        var top = measure.Top;
        var left = measure.Left;

        if (positionY != "top")
        {
            top += measure.Height / 2;
        }

        left += measure.Width / 2;

        // A sheet that is not laid out yet counts with the original's guesses.
        var height = measure.DialogHeight > 0 ? measure.DialogHeight : 300;
        var width = measure.DialogWidth > 0 ? measure.DialogWidth : 160;

        top -= height / 2;
        left -= width / 2;

        var overflowX = left + width - measure.WindowWidth;
        var overflowY = top + height - measure.WindowHeight;

        if (overflowX > 0)
        {
            left -= overflowX + 20;
        }

        if (overflowY > 0)
        {
            top -= overflowY + 20;
        }

        top += offsetTop;
        left += offsetLeft;

        return (Math.Max(left, 10), Math.Max(top, 10));
    }
}

/// <summary>Moves an action sheet to its anchor (<see cref="ActionSheetOptions.PositionTo"/>).</summary>
public interface IActionSheetPositioner
{
    /// <summary>
    /// Positions the dialog that contains <paramref name="content"/>. Does nothing when the options name
    /// no anchor or the anchor is not in the document any more: the sheet then stays centred.
    /// </summary>
    ValueTask PositionAsync(ElementReference content, ActionSheetOptions options);
}

/// <summary>
/// <see cref="IActionSheetPositioner"/> over <c>js/actionsheet.js</c>. The module is loaded at startup
/// because it also remembers the element clicked last (<see cref="ActionSheetAnchor.LastClicked"/>).
/// </summary>
[AppService(typeof(IActionSheetPositioner))]
public sealed class ActionSheetPositioner(IJSRuntime js) : IActionSheetPositioner, IStartupTask
{
    private Task<IJSObjectReference?>? _module;

    public async Task StartAsync()
    {
        try
        {
            await GetModuleAsync();
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Loading the action sheet module failed: " + exception.Message);
        }
    }

    public async ValueTask PositionAsync(ElementReference content, ActionSheetOptions options)
    {
        if (options.PositionTo is not { } anchor)
        {
            return;
        }

        try
        {
            var module = await GetModuleAsync();
            if (module is null)
            {
                return;
            }

            var measure = anchor.Element is { } element
                ? await module.InvokeAsync<ActionSheetMeasure?>("measure", content, element)
                : await module.InvokeAsync<ActionSheetMeasure?>("measure", content, null);
            if (measure is null)
            {
                return;
            }

            var (left, top) = ActionSheetPosition.Compute(measure.Value, options.PositionY, options.OffsetLeft, options.OffsetTop);
            await module.InvokeVoidAsync("place", content, left, top);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Positioning an action sheet failed: " + exception.Message);
        }
    }

    private static bool IsInteropFailure(Exception exception) =>
        exception is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;

    private Task<IJSObjectReference?> GetModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync() =>
        await js.InvokeAsync<IJSObjectReference?>("import", "./js/actionsheet.js");
}
