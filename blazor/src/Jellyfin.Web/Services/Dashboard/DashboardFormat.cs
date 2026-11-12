using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>Small formatting helpers shared by the dashboard's tables and forms.</summary>
public static class DashboardFormat
{
    // date-fns' "Pp" (short date and time), which the original's table cells use.
    private static readonly object DateTimeOptions =
        new { year = "numeric", month = "2-digit", day = "2-digit", hour = "numeric", minute = "2-digit" };

    /// <summary>A date and time for a table cell ("04/29/2024, 8:30 PM"); empty without a date.</summary>
    public static string DateTime(Globalize globalize, DateTimeOffset? date) =>
        date is { } value ? globalize.FormatDate(value, DateTimeOptions) : string.Empty;

    /// <summary>What a device is called: the name its owner gave it, else its own.</summary>
    public static string DeviceName(DeviceInfoDto device) =>
        string.IsNullOrEmpty(device.CustomName) ? device.Name ?? string.Empty : device.CustomName;

    /// <summary>"Jellyfin Web 10.11.0": the app and its version, leaving out what is missing.</summary>
    public static string AppText(DeviceInfoDto device) =>
        string.Join(' ', new[] { device.AppName, device.AppVersion }.Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>Whether any of the texts contains the search text (always true for an empty search).</summary>
    public static bool Matches(string? search, params string?[] texts) =>
        string.IsNullOrWhiteSpace(search)
        || texts.Any(text => text?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>
    /// The enum values with the given names, typed like <paramref name="target"/>: the generated
    /// client declares the enums of its query parameters next to each request builder, and this
    /// picks the right one from the property that is assigned.
    /// </summary>
    public static T[] Enums<T>(T[]? target, params string[] names)
        where T : struct, Enum
    {
        _ = target;
        return names.Select(name => Enum.Parse<T>(name)).ToArray();
    }

    /// <summary>The enum value with the given name, typed like <paramref name="target"/>; null for an empty or unknown name.</summary>
    public static T? EnumOrNull<T>(T? target, string? name)
        where T : struct, Enum
    {
        _ = target;
        return Enum.TryParse<T>(name, out var value) ? value : null;
    }
}
