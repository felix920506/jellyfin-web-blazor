using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>
/// Translations and locale. Port of lib/globalize: dictionaries are the original
/// <c>src/strings/*.json</c> files, lookup falls back to en-us and then to the key itself.
/// </summary>
/// <remarks>
/// In components: <c>@inject Globalize L</c>, then <c>@L["ButtonOk"]</c> or
/// <c>@L.Translate("DeleteName", item.Name)</c>.
/// </remarks>
public sealed partial class Globalize(AppAssets assets, IBrowser browser)
{
    public const string FallbackCulture = "en-us";

    /// <summary>The cultures with a dictionary (file names of src/strings without ".json").</summary>
    public static readonly IReadOnlyList<string> Locales =
    [
        "af", "ar", "be-by", "bg-bg", "bn_bd", "ca", "cs", "cy", "da", "de", "el", "en-gb", "en-us", "eo", "es", "es_419",
        "es-ar", "es_do", "es-mx", "et", "eu", "fa", "fi", "fil", "fr", "fr-ca", "gl", "gsw", "he", "hi-in", "hr", "hu", "id",
        "it", "ja", "kk", "ko", "lt-lt", "lv", "mr", "ms", "nb", "nl", "nn", "pl", "pr", "pt", "pt-br", "pt-pt", "ro", "ru",
        "sk", "sl-si", "sq", "sv", "ta", "th", "tr", "uk", "ur_pk", "vi", "zh-cn", "zh-hk", "zh-tw"
    ];

    private static readonly string[] RtlLanguages = ["ar", "fa", "ur", "he"];

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _dictionaries = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _current = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _fallback = new Dictionary<string, string>();
    private string? _userLanguage;
    private string? _userDateTimeLocale;

    /// <summary>Raised after the UI language changed; the router re-creates the visible page.</summary>
    public event Action? LanguageChanged;

    /// <summary>The UI culture, lower case with '-' separators, e.g. "en-us" or "de".</summary>
    public string Culture { get; private set; } = FallbackCulture;

    /// <summary>The locale for dates and numbers: the user's choice, or the UI culture.</summary>
    public string DateTimeLocale => string.IsNullOrEmpty(_userDateTimeLocale) ? Culture : _userDateTimeLocale;

    public bool IsRtl { get; private set; }

    public string this[string key] => Translate(key);

    /// <summary>Translates a key and replaces <c>{0}</c>, <c>{1}</c>... with the arguments.</summary>
    public string Translate(string key, params object?[] args)
    {
        var hash = key.IndexOf('#');
        var name = hash >= 0 ? key[(hash + 1)..] : key;

        if (!_current.TryGetValue(name, out var text) && !_fallback.TryGetValue(name, out text))
        {
            text = name;
        }

        if (args.Length == 0)
        {
            return text;
        }

        return PlaceholderRegex().Replace(text, match =>
        {
            var index = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
            return index < args.Length ? FormatArgument(args[index]) : match.Value;
        });
    }

    /// <summary>True when the key has a translation (in the current culture or en-us).</summary>
    public bool Has(string key) => _current.ContainsKey(key) || _fallback.ContainsKey(key);

    /// <summary>Loads the dictionaries for the browser's language. Called once at startup.</summary>
    public Task InitializeAsync() => ApplyCultureAsync(force: true);

    /// <summary>Applies the user's language settings (null means "use the browser's language").</summary>
    public Task SetUserCultureAsync(string? language, string? dateTimeLocale)
    {
        _userLanguage = language;
        _userDateTimeLocale = string.IsNullOrEmpty(dateTimeLocale) ? null : Normalize(dateTimeLocale);
        return ApplyCultureAsync(force: false);
    }

    /// <summary>Port of getCurrentDateTimeLocale-based formatting: <c>toLocaleString(locale, options)</c>.</summary>
    public string FormatDate(DateTimeOffset value, object? options = null) => browser.FormatDate(value, DateTimeLocale, options);

    public string FormatNumber(double value, object? options = null) => browser.FormatNumber(value, DateTimeLocale, options);

    /// <summary>Registers a dictionary directly (tests, plugins).</summary>
    public void AddDictionary(string culture, IReadOnlyDictionary<string, string> dictionary)
    {
        culture = Normalize(culture);
        _dictionaries[culture] = dictionary;
        if (culture == FallbackCulture)
        {
            _fallback = dictionary;
        }

        if (culture == Culture)
        {
            _current = dictionary;
        }
    }

    /// <summary>Port of the dictionary lookup: exact culture, then base language, then en-us.</summary>
    public static string ResolveDictionaryCulture(string culture)
    {
        culture = Normalize(culture);
        var exact = Locales.FirstOrDefault(l => Normalize(l) == culture);
        if (exact is not null)
        {
            return exact;
        }

        var baseLanguage = culture.Split('-')[0];
        return Locales.FirstOrDefault(l => Normalize(l) == baseLanguage) ?? FallbackCulture;
    }

    public static string Normalize(string culture) => culture.Replace('_', '-').ToLowerInvariant();

    private async Task ApplyCultureAsync(bool force)
    {
        var culture = !string.IsNullOrEmpty(_userLanguage)
            ? _userLanguage
            : browser.Environment.Languages.FirstOrDefault(l => !string.IsNullOrEmpty(l)) ?? FallbackCulture;
        culture = Normalize(culture);

        if (!force && culture == Culture)
        {
            return;
        }

        _fallback = await LoadDictionaryAsync(FallbackCulture);
        var file = ResolveDictionaryCulture(culture);
        _current = Normalize(file) == FallbackCulture ? _fallback : await LoadDictionaryAsync(file);

        var changed = culture != Culture;
        Culture = culture;
        IsRtl = RtlLanguages.Any(l => culture.Contains(l, StringComparison.Ordinal));

        browser.SetAttribute(DocumentTarget.Html, "lang", culture);
        browser.SetAttribute(DocumentTarget.Html, "dir", IsRtl ? "rtl" : "ltr");
        browser.SetAttribute(DocumentTarget.Body, "dir", IsRtl ? "rtl" : "ltr");
        browser.EnableStylesheet("cssRtl", IsRtl);

        if (changed && !force)
        {
            LanguageChanged?.Invoke();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadDictionaryAsync(string file)
    {
        var key = Normalize(file);
        if (_dictionaries.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            var loaded = await assets.Http.GetFromJsonAsync<Dictionary<string, string>>($"generated/strings/{file}.json");
            if (loaded is not null)
            {
                _dictionaries[key] = loaded;
                return loaded;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            AppLog.Warn($"[Globalize] failed to load strings for {file}: {ex.Message}");
        }

        return new Dictionary<string, string>();
    }

    private string FormatArgument(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        int or long or double or float or decimal => FormatNumberSafe(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private string FormatNumberSafe(double value)
    {
        try
        {
            return browser.FormatNumber(value, Culture);
        }
        catch (Exception)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex PlaceholderRegex();
}
