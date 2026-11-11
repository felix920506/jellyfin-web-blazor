using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;

namespace Jellyfin.Web.Services;

/// <summary>Converts SDK enums to and from the strings the server API uses (<c>EnumMember</c> values).</summary>
public static class EnumText
{
    private static readonly ConcurrentDictionary<Type, (Dictionary<string, string> ToText, Dictionary<string, object> FromText)> Maps = new();

    /// <summary>The API string of an enum value, e.g. <c>CollectionType.Tvshows</c> is "tvshows".</summary>
    public static string ToApiString(this Enum value)
    {
        var name = value.ToString();
        return MapOf(value.GetType()).ToText.TryGetValue(name, out var text) ? text : name;
    }

    /// <summary>The API string of a nullable enum value, or null.</summary>
    public static string? ToApiString<T>(this T? value)
        where T : struct, Enum => value?.ToApiString();

    /// <summary>Parses an API string (or member name), ignoring case.</summary>
    public static T? Parse<T>(string? text)
        where T : struct, Enum
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return MapOf(typeof(T)).FromText.TryGetValue(text, out var value) ? (T)value : null;
    }

    private static (Dictionary<string, string> ToText, Dictionary<string, object> FromText) MapOf(Type type) =>
        Maps.GetOrAdd(type, static t =>
        {
            var toText = new Dictionary<string, string>(StringComparer.Ordinal);
            var fromText = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var text = field.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? field.Name;
                var value = field.GetValue(null)!;
                toText[field.Name] = text;
                fromText[field.Name] = value;
                fromText[text] = value;
            }

            return (toText, fromText);
        });
}
