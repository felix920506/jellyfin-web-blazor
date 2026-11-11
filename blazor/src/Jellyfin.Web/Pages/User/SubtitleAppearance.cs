using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Web.Services;

namespace Jellyfin.Web.Pages.User;

/// <summary>
/// The subtitle appearance of this device: the object userSettings.getSubtitleAppearanceSettings stores
/// as JSON, and its styles (components/subtitlesettings/subtitleappearancehelper).
/// </summary>
public sealed class SubtitleAppearance
{
    /// <summary>The settings key of the appearance used by the players of this device.</summary>
    public const string SettingsKey = "localplayersubtitleappearance3";

    private const double LineHeight = 1.35;

    /// <summary>The stored object; fields this page does not know are kept as they are.</summary>
    public JsonObject Values { get; private init; } = [];

    public string? AspectMode { get => Text("aspectMode"); set => Values["aspectMode"] = value; }

    public string? SubtitleStyling { get => Text("subtitleStyling"); set => Values["subtitleStyling"] = value; }

    public string? TextSize { get => Text("textSize"); set => Values["textSize"] = value; }

    public string? TextWeight { get => Text("textWeight"); set => Values["textWeight"] = value; }

    public string? DropShadow { get => Text("dropShadow"); set => Values["dropShadow"] = value; }

    public string? Font { get => Text("font"); set => Values["font"] = value; }

    public string? TextBackground { get => Text("textBackground"); set => Values["textBackground"] = value; }

    public string? TextColor { get => Text("textColor"); set => Values["textColor"] = value; }

    /// <summary>Lines from the top (0 and up) or from the bottom (below 0) of the screen.</summary>
    public string? VerticalPosition { get => Text("verticalPosition"); set => Values["verticalPosition"] = value; }

    /// <summary>The vertical position as a number (parseInt), null when it is none.</summary>
    public int? VerticalPositionValue =>
        double.TryParse(VerticalPosition, NumberStyles.Float, CultureInfo.InvariantCulture, out var position) ? (int)Math.Truncate(position) : null;

    /// <summary>Reads the stored JSON over the defaults (vertical position -3, bitmap subtitles contained).</summary>
    public static SubtitleAppearance Parse(string? json)
    {
        var values = new JsonObject { ["verticalPosition"] = -3, ["aspectMode"] = "contain" };
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                if (JsonNode.Parse(json) is JsonObject stored)
                {
                    foreach (var (name, value) in stored)
                    {
                        values[name] = value?.DeepClone();
                    }
                }
            }
            catch (JsonException)
            {
                // Unreadable settings are replaced by the defaults.
            }
        }

        return new SubtitleAppearance { Values = values };
    }

    /// <summary>The appearance stored in the user's settings of this device.</summary>
    public static SubtitleAppearance Load(UserSettings settings) => Parse(settings.Get(SettingsKey, false));

    /// <summary>Stores the appearance in the user's settings of this device.</summary>
    public void Save(UserSettings settings) => settings.Set(SettingsKey, ToJson(), false);

    public string ToJson() => Values.ToJsonString();

    /// <summary>The styles of the subtitle text; the settings preview leaves out the vertical position.</summary>
    public IReadOnlyList<(string Name, string Value)> TextStyles(bool preview)
    {
        var list = new List<(string Name, string Value)>
        {
            ("font-size", TextSize switch
            {
                "smaller" => ".8em",
                "small" => "inherit",
                "larger" => "2em",
                "extralarge" => "2.2em",
                "large" => "1.72em",
                _ => "1.36em"
            }),
            ("font-weight", TextWeight == "bold" ? "bold" : "normal"),
            ("text-shadow", DropShadow switch
            {
                "raised" => "-0.04em -0.04em #fff, 0px -0.04em #fff, -0.04em 0px #fff, 0.04em 0.04em #000, 0px 0.04em #000, 0.04em 0px #000",
                "depressed" => "0.04em 0.04em #fff, 0px 0.04em #fff, 0.04em 0px #fff, -0.04em -0.04em #000, 0px -0.04em #000, -0.04em 0px #000",
                "uniform" => "#000 0px 0.03em, #000 0px -0.03em, #000 0px 0.05em, #000 0px -0.05em, #000 0.03em 0px, #000 -0.03em 0px, "
                    + "#000 0.03em 0.03em, #000 -0.03em 0.03em, #000 0.03em -0.03em, #000 -0.03em -0.03em, #000 0.03em 0.05em, "
                    + "#000 -0.03em 0.05em, #000 0.03em -0.05em, #000 -0.03em -0.05em, #000 0.05em 0px, #000 -0.05em 0px, "
                    + "#000 0.05em 0.03em, #000 -0.05em 0.03em, #000 0.05em -0.03em, #000 -0.05em -0.03em",
                "none" => "none",
                _ => "#000000 0px 0px 7px"
            }),
            ("background-color", string.IsNullOrEmpty(TextBackground) ? "transparent" : TextBackground),
            ("color", string.IsNullOrEmpty(TextColor) ? "#ffffff" : TextColor),
            ("font-family", Font switch
            {
                "typewriter" => "\"Courier New\",monospace",
                "print" => "Georgia,Times New Roman,Arial,Helvetica,serif",
                "console" => "Consolas,Lucida Console,Menlo,Monaco,monospace",
                "cursive" => "Lucida Handwriting,Brush Script MT,Segoe Script,cursive,Quintessential,system-ui,-apple-system,BlinkMacSystemFont,sans-serif",
                "casual" => "Gabriola,Segoe Print,Comic Sans MS,Chalkboard,Short Stack,system-ui,-apple-system,BlinkMacSystemFont,sans-serif",
                "smallcaps" => "Copperplate Gothic,Copperplate Gothic Bold,Copperplate,system-ui,-apple-system,BlinkMacSystemFont,sans-serif",
                _ => "-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif,Apple Color Emoji,Segoe UI Emoji,Segoe UI Symbol"
            }),
            ("font-variant", Font == "smallcaps" ? "small-caps" : "none")
        };

        if (!preview)
        {
            var position = VerticalPositionValue;
            if (position < 0)
            {
                list.Add(("margin-bottom", Em(Math.Abs(position.Value + 1) * LineHeight)));
                list.Add(("margin-top", string.Empty));
            }
            else
            {
                list.Add(("margin-bottom", string.Empty));
                list.Add(("margin-top", position is null ? string.Empty : Em(position.Value * LineHeight)));
            }
        }

        return list;
    }

    /// <summary>The styles of the window the subtitle text sits in: at the bottom for negative positions.</summary>
    public IReadOnlyList<(string Name, string Value)> WindowStyles(bool preview)
    {
        if (preview)
        {
            return [];
        }

        return VerticalPositionValue < 0
            ? [("top", string.Empty), ("bottom", "0")]
            : [("top", "0"), ("bottom", string.Empty)];
    }

    /// <summary>A style attribute value of the styles that have a value.</summary>
    public static string ToCss(IEnumerable<(string Name, string Value)> styles)
    {
        var css = new StringBuilder();
        foreach (var (name, value) in styles)
        {
            if (value.Length != 0)
            {
                css.Append(name).Append(':').Append(value).Append(';');
            }
        }

        return css.ToString();
    }

    private static string Em(double value) => value.ToString("0.####", CultureInfo.InvariantCulture) + "em";

    private string? Text(string name) => Values[name] switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        var node => node.ToJsonString()
    };
}
