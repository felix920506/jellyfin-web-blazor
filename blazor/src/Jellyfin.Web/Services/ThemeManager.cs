using Jellyfin.Web.Interop;
using MudBlazor;

namespace Jellyfin.Web.Services;

/// <summary>
/// Applies a theme: the original theme stylesheet (<c>generated/themes/{id}/theme.css</c>), the
/// <c>data-theme</c> attribute, the browser theme colour and the matching MudBlazor palette.
/// Port of scripts/themeManager.js, components/ThemeCss.tsx and themes/*.ts.
/// </summary>
public sealed class ThemeManager(WebConfig config, UserSettings userSettings, IBrowser browser)
{
    private static readonly string[] FontFamily = ["Noto Sans", "sans-serif"];

    private static readonly Dictionary<string, Scheme> Schemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dark"] = new(true, "#00a4dc", "#00a4dc", "#101010", "#202020", "#202020", "#ffffff", "rgba(255,255,255,0.7)"),
        ["light"] = new(false, "#00a4dc", "#00a4dc", "#f2f2f2", "#e8e8e8", "#e8e8e8", "#000000", "rgba(0,0,0,0.87)"),
        ["appletv"] = new(false, "#00a4dc", "#00a4dc", "#d5e9f2", "#eaeaea", "#bcbcbc", "rgba(0,0,0,0.87)", "rgba(0,0,0,0.87)"),
        ["blueradiance"] = new(true, "#00a4dc", "#00a4dc", "#033361", "#011432", "#011432", "#eeeeee", "rgba(255,255,255,0.9)"),
        ["purplehaze"] = new(true, "#48c3c8", "#ff77f1", "#230c33", "#230c33", "#230c33", "#f8f8fe", "rgba(248,248,254,0.973)"),
        ["wmc"] = new(true, "#00a4dc", "#00a4dc", "#0f3562", "#0c2450", "#0c2450", "#eeeeee", "rgba(255,255,255,0.9)"),
        ["catppuccin-latte"] = new(false, "#209fb5", "#209fb5", "#eff1f5", "#e6e9ef", "#dce0e8", "#4c4f69", "#5c5f77", "#d20f39"),
        ["catppuccin-frappe"] = new(true, "#85c1dc", "#85c1dc", "#303446", "#292c3c", "#232634", "#c6d0f5", "#b5bfe2", "#e78284"),
        ["catppuccin-macchiato"] = new(true, "#7dc4e4", "#7dc4e4", "#24273a", "#1e2030", "#181926", "#cad3f5", "#b8c0e0", "#ed8796"),
        ["catppuccin-mocha"] = new(true, "#74c7ec", "#74c7ec", "#1e1e2e", "#181825", "#11111b", "#cdd6f4", "#bac2de", "#f38ba8")
    };

    private readonly Dictionary<string, MudTheme> _mudThemes = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    /// <summary>The applied theme id.</summary>
    public string CurrentId { get; private set; } = "dark";

    public bool IsDark { get; private set; } = true;

    public MudTheme MudTheme { get; private set; } = BuildMudTheme(Schemes["dark"]);

    /// <summary>Applies the signed-in user's theme (or the dashboard theme), else the default.</summary>
    public void ApplyUserTheme(bool dashboard = false) => SetTheme(dashboard ? userSettings.DashboardTheme : userSettings.Theme);

    /// <summary>Applies a theme by id; unknown or null ids select the configured default.</summary>
    public void SetTheme(string? id)
    {
        var option = config.Themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? config.DefaultTheme;
        var scheme = Schemes.TryGetValue(option.Id, out var known) ? known : Schemes["dark"];

        if (!_mudThemes.TryGetValue(option.Id, out var mudTheme))
        {
            mudTheme = _mudThemes[option.Id] = BuildMudTheme(scheme);
        }

        var changed = !string.Equals(CurrentId, option.Id, StringComparison.Ordinal) || !ReferenceEquals(MudTheme, mudTheme);
        CurrentId = option.Id;
        IsDark = scheme.Dark;
        MudTheme = mudTheme;

        browser.SetAttribute(DocumentTarget.Html, "data-theme", option.Id);
        browser.SetStylesheet("cssTheme", $"generated/themes/{option.Id}/theme.css");
        if (!string.IsNullOrEmpty(option.Color))
        {
            browser.SetThemeColor(option.Color);
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private static MudTheme BuildMudTheme(Scheme s)
    {
        var theme = new MudTheme
        {
            Typography = new Typography
            {
                Default = new DefaultTypography { FontFamily = FontFamily },
                Button = new ButtonTypography { FontFamily = FontFamily, TextTransform = "none" },
                H1 = new H1Typography { FontFamily = FontFamily, FontSize = "1.8rem" },
                H2 = new H2Typography { FontFamily = FontFamily, FontSize = "1.5rem" },
                H3 = new H3Typography { FontFamily = FontFamily, FontSize = "1.17rem" }
            },
            LayoutProperties = new LayoutProperties { AppbarHeight = "48px", DrawerWidthLeft = "240px" }
        };

        Palette palette = s.Dark ? theme.PaletteDark : theme.PaletteLight;
        palette.Primary = s.Primary;
        palette.Secondary = s.Secondary;
        palette.Background = s.Background;
        palette.Surface = s.Paper;
        palette.AppbarBackground = s.AppBar;
        palette.AppbarText = s.Text;
        palette.DrawerBackground = s.Paper;
        palette.DrawerText = s.Text;
        palette.DrawerIcon = s.Text;
        palette.TextPrimary = s.Text;
        palette.TextSecondary = s.TextSecondary;
        palette.ActionDefault = s.Text;
        palette.Error = s.Error;
        return theme;
    }

    private sealed record Scheme(
        bool Dark,
        string Primary,
        string Secondary,
        string Background,
        string Paper,
        string AppBar,
        string Text,
        string TextSecondary,
        string Error = "#c62828");
}
