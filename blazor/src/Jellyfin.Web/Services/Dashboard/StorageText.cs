using System.Globalization;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>How full a server folder's drive is, for the paths widget.</summary>
/// <param name="Percent">0..100, 0 when the size is unknown.</param>
/// <param name="Level">"error" from 90 %, "warning" from 80 %, otherwise "success".</param>
/// <param name="Text">"1.5 GiB / 10.0 GiB", or "? / ?" when unknown.</param>
public sealed record StorageUsage(double Percent, string Level, string Text);

/// <summary>Sizes and drive usage as text. Port of utils/file.ts and StorageListItem.tsx.</summary>
public static class StorageText
{
    private const string Units = "KMGTPEZY";

    /// <summary>"1.5 GiB". Port of getReadableSize().</summary>
    public static string ReadableSize(double value, int precision = 1)
    {
        var exponent = value > 0 ? (int)Math.Floor(Math.Log(value) / Math.Log(1024)) : 0;
        exponent = Math.Clamp(exponent, 0, Units.Length);
        var number = (value / Math.Pow(1024, exponent)).ToString("F" + precision, CultureInfo.InvariantCulture);
        return number + " " + (exponent > 0 ? Units[exponent - 1] + "iB" : "Bytes");
    }

    public static StorageUsage Usage(FolderStorageDto? folder)
    {
        var used = folder?.UsedSpace;
        var free = folder?.FreeSpace;
        var known = used is >= 0;
        var total = known ? Math.Max(0, free ?? 0) + Math.Max(0, used!.Value) : -1;
        var percent = total <= 0 ? 0 : Math.Min(100, used!.Value / (double)total * 100);
        var level = percent >= 90 ? "error" : percent >= 80 ? "warning" : "success";
        var text = (known ? ReadableSize(used!.Value) : "?") + " / " + (total < 0 ? "?" : ReadableSize(total));
        return new StorageUsage(percent, level, text);
    }

    /// <summary>The translation key of a drive type's name, or null for a type the client does not know.</summary>
    public static string? TypeKey(string? storageType) => storageType switch
    {
        "CDRom" or "Fixed" or "Network" or "NoRootDirectory" or "Ram" or "Removable" or "Unknown" => "StorageType." + storageType,
        _ => null,
    };

    /// <summary>The material icon for a drive type (the server's <c>DriveType</c> names).</summary>
    public static string Icon(string? storageType) => storageType switch
    {
        "CDRom" => "album",
        "Network" => "lan",
        "Ram" => "memory",
        "Removable" => "usb",
        _ => "storage",
    };
}
