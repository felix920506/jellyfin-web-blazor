using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Jellyfin.Web.Routing;

/// <summary>A page matched for a path, with the values to pass to its parameters.</summary>
public sealed record RouteMatch(Type PageType, AccessLevel Access, IReadOnlyDictionary<string, string> RouteValues);

/// <summary>
/// Routes collected from <c>@page</c> directives. Templates support literal segments,
/// <c>{parameter}</c> segments and a trailing <c>{*catchAll}</c>; matching ignores case.
/// </summary>
public sealed class RouteTable
{
    private readonly List<Entry> _entries = [];

    public RouteTable(params Assembly[] assemblies)
    {
        foreach (var type in assemblies.SelectMany(a => a.GetExportedTypes()))
        {
            if (!typeof(IComponent).IsAssignableFrom(type) || type.IsAbstract)
            {
                continue;
            }

            foreach (var route in type.GetCustomAttributes<RouteAttribute>(inherit: false))
            {
                Add(route.Template, type);
            }
        }

        Sort();
    }

    /// <summary>For tests: a table with explicit entries.</summary>
    internal RouteTable(IEnumerable<(string Template, Type Page)> routes)
    {
        foreach (var (template, page) in routes)
        {
            Add(template, page);
        }

        Sort();
    }

    public IEnumerable<string> Templates => _entries.Select(e => e.Template);

    public RouteMatch? Match(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in _entries)
        {
            if (entry.TryMatch(segments, out var values))
            {
                return new RouteMatch(entry.Page, entry.Access, values);
            }
        }

        return null;
    }

    private void Add(string template, Type page)
    {
        var access = page.GetCustomAttribute<AccessAttribute>()?.Level ?? AccessLevel.User;
        _entries.Add(new Entry(template, page, access));
    }

    // Most specific first: more literal segments win, catch-all routes go last.
    private void Sort() => _entries.Sort((a, b) =>
    {
        var byCatchAll = a.HasCatchAll.CompareTo(b.HasCatchAll);
        if (byCatchAll != 0)
        {
            return byCatchAll;
        }

        var byLiterals = b.LiteralCount.CompareTo(a.LiteralCount);
        return byLiterals != 0 ? byLiterals : string.CompareOrdinal(a.Template, b.Template);
    });

    private sealed class Entry
    {
        private readonly string[] _segments;

        public Entry(string template, Type page, AccessLevel access)
        {
            Template = template;
            Page = page;
            Access = access;
            _segments = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
            HasCatchAll = _segments.Length > 0 && _segments[^1].StartsWith("{*", StringComparison.Ordinal);
            LiteralCount = _segments.Count(s => !s.StartsWith('{'));
        }

        public string Template { get; }
        public Type Page { get; }
        public AccessLevel Access { get; }
        public bool HasCatchAll { get; }
        public int LiteralCount { get; }

        public bool TryMatch(string[] path, out IReadOnlyDictionary<string, string> values)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            values = result;

            var fixedCount = HasCatchAll ? _segments.Length - 1 : _segments.Length;
            if (path.Length < fixedCount || (!HasCatchAll && path.Length != fixedCount))
            {
                return false;
            }

            for (var i = 0; i < fixedCount; i++)
            {
                var segment = _segments[i];
                if (segment.StartsWith('{'))
                {
                    result[ParameterName(segment)] = Uri.UnescapeDataString(path[i]);
                }
                else if (!string.Equals(segment, path[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (HasCatchAll)
            {
                result[ParameterName(_segments[^1])] = string.Join('/', path.Skip(fixedCount).Select(Uri.UnescapeDataString));
            }

            return true;
        }

        private static string ParameterName(string segment)
        {
            var name = segment.Trim('{', '}').TrimStart('*');
            var colon = name.IndexOf(':');
            return (colon >= 0 ? name[..colon] : name).TrimEnd('?');
        }
    }
}

/// <summary>Binds route and query values to a page's <c>[Parameter]</c> properties by name.</summary>
public static class PageParameters
{
    private static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> Cache = [];

    public static Dictionary<string, object?> Bind(Type pageType, IReadOnlyDictionary<string, string> routeValues, IReadOnlyDictionary<string, string> query)
    {
        var properties = PropertiesOf(pageType);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Route values win over query values of the same name.
        foreach (var source in new[] { query, routeValues })
        {
            foreach (var (key, text) in source)
            {
                if (properties.TryGetValue(key, out var property) && TryConvert(text, property.PropertyType, out var value))
                {
                    result[property.Name] = value;
                }
            }
        }

        // Absent values reset the parameter, so a kept page never sees a stale value
        // (e.g. ?tab=1 → no tab). Bound parameters therefore cannot have initialisers.
        foreach (var property in properties.Values)
        {
            if (!result.ContainsKey(property.Name) && IsBindable(property.PropertyType))
            {
                result[property.Name] = property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null;
            }
        }

        return result;
    }

    private static bool IsBindable(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        return target == typeof(string) || target == typeof(Guid) || target == typeof(bool) || target == typeof(int)
            || target == typeof(long) || target == typeof(double) || target.IsEnum;
    }

    internal static bool TryConvert(string text, Type type, out object? value)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        value = null;

        if (target == typeof(string))
        {
            value = text;
            return true;
        }

        if (text.Length == 0)
        {
            return false;
        }

        if (target == typeof(Guid))
        {
            var ok = Guid.TryParse(text, out var guid);
            value = guid;
            return ok;
        }

        if (target == typeof(bool))
        {
            var ok = bool.TryParse(text, out var flag);
            value = flag;
            return ok;
        }

        if (target == typeof(int))
        {
            var ok = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number);
            value = number;
            return ok;
        }

        if (target == typeof(long))
        {
            var ok = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number);
            value = number;
            return ok;
        }

        if (target == typeof(double))
        {
            var ok = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number);
            value = number;
            return ok;
        }

        if (target.IsEnum)
        {
            var ok = Enum.TryParse(target, text, ignoreCase: true, out var parsed);
            value = parsed;
            return ok;
        }

        return false;
    }

    private static Dictionary<string, PropertyInfo> PropertiesOf(Type pageType)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(pageType, out var properties))
            {
                properties = pageType
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
                    .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
                Cache[pageType] = properties;
            }

            return properties;
        }
    }
}
