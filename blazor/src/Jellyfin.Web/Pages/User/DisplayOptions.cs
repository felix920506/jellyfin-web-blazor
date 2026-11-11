using Jellyfin.Web.Services;

namespace Jellyfin.Web.Pages.User;

/// <summary>The option lists of the display settings (components/displaySettings).</summary>
public static class DisplayOptions
{
    /// <summary>The display languages of the original template, without its leading "Auto" entry.</summary>
    public static readonly IReadOnlyList<SelectOption> Languages =
    [
        new("af", "Afrikaans"),
        new("ar", "العربية"),
        new("be-BY", "Беларуская"),
        new("bg-BG", "Български"),
        new("bn_BD", "বাংলা (বাংলাদেশ)"),
        new("ca", "Català"),
        new("cs", "Čeština"),
        new("cy", "Cymraeg"),
        new("da", "Dansk"),
        new("de", "Deutsch"),
        new("el", "Ελληνικά"),
        new("en-GB", "English (United Kingdom)"),
        new("en-US", "English"),
        new("eo", "Esperanto"),
        new("es", "Español"),
        new("es_419", "Español americano"),
        new("es-AR", "Español (Argentina)"),
        new("es_DO", "Español (Dominicana)"),
        new("es-MX", "Español (México)"),
        new("et", "Eesti"),
        new("eu", "Euskara"),
        new("fa", "فارسی"),
        new("fi", "Suomi"),
        new("fil", "Filipino"),
        new("fr", "Français"),
        new("fr-CA", "Français (Canada)"),
        new("gl", "Galego"),
        new("gsw", "Schwiizerdütsch"),
        new("he", "עִבְרִית"),
        new("hi-IN", "हिन्दी"),
        new("hr", "Hrvatski "),
        new("hu", "Magyar"),
        new("id", "Bahasa Indonesia"),
        new("is-IS", "Íslenska"),
        new("it", "Italiano"),
        new("ja", "日本語"),
        new("kk", "Qazaqşa"),
        new("ko", "한국어"),
        new("lt-LT", "Lietuvių"),
        new("lv", "Latviešu"),
        new("mk", "Македонски"),
        new("ml", "മലയാളം"),
        new("mr", "मराठी"),
        new("ms", "Bahasa Melayu"),
        new("nb", "Norsk bokmål"),
        new("ne", "नेपाली"),
        new("nl", "Nederlands"),
        new("nn", "Norsk nynorsk"),
        new("pa", "ਪੰਜਾਬੀ"),
        new("pl", "Polski"),
        new("pr", "Pirate"),
        new("pt", "Português"),
        new("pt-BR", "Português (Brasil)"),
        new("pt-PT", "Português (Portugal)"),
        new("ro", "Românește"),
        new("ru", "Русский"),
        new("sk", "Slovenčina"),
        new("sl-SI", "Slovenščina"),
        new("sq", "Shqip"),
        new("sr", "Српски"),
        new("sv", "Svenska"),
        new("ta", "தமிழ்"),
        new("te", "తెలుగు"),
        new("th", "ภาษาไทย"),
        new("tr", "Türkçe"),
        new("uk", "Українська"),
        new("ur_PK", "اُردُو"),
        new("vi", "Tiếng Việt"),
        new("zh-CN", "汉语 (简化字)"),
        new("zh-TW", "漢語 (繁体字)"),
        new("zh-HK", "廣東話 (香港)")
    ];

    /// <summary>The date and time locales: the display languages except Basque, as in the original template.</summary>
    public static readonly IReadOnlyList<SelectOption> DateTimeLocales = Languages.Where(language => language.Value != "eu").ToList();

    /// <summary>The screensaver plugins of the original client (id, name key, entry in config.json's plugin list).</summary>
    public static readonly IReadOnlyList<(string Id, string NameKey, string Plugin)> Screensavers =
    [
        ("backdropscreensaver", "BackdropScreensaver", "backdropScreensaver/plugin"),
        ("logoscreensaver", "LogoScreensaver", "logoScreensaver/plugin")
    ];

    public static List<SelectOption> WithAuto(IEnumerable<SelectOption> options, Globalize globalize) =>
        [new SelectOption(string.Empty, globalize["Auto"]), .. options];

    /// <summary>
    /// The layouts of the port. The original offers its legacy desktop, legacy mobile and TV
    /// layouts here; the port has the modern layout only, in a desktop and a mobile variant.
    /// </summary>
    public static List<SelectOption> Layouts(Globalize globalize) =>
    [
        new(string.Empty, globalize["Auto"]),
        new("desktop", globalize["Desktop"]),
        new("mobile", globalize["Mobile"])
    ];

    /// <summary>The select value for a stored layout setting ("auto" and unknown values select "Auto").</summary>
    public static string LayoutValue(string? setting) => setting is "desktop" or "mobile" ? setting : string.Empty;

    /// <summary>"None" and the screensavers among the configured plugins (all of them when no plugin list is known).</summary>
    public static List<SelectOption> ScreensaverOptions(Globalize globalize, IReadOnlyCollection<string>? plugins = null) =>
    [
        new SelectOption("none", globalize["None"]),
        .. Screensavers
            .Where(s => plugins is null || plugins.Count == 0 || plugins.Contains(s.Plugin))
            .Select(s => new SelectOption(s.Id, globalize[s.NameKey]))
    ];
}
