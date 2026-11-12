using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>The icon of a client app or device. Port of getDeviceIcon() in utils/image.ts.</summary>
public static class DeviceIcons
{
    private const string Base = "generated/assets/img/devices/";

    public static string For(SessionInfoDto session) =>
        For(session.Client, session.DeviceName, session.Capabilities?.IconUrl);

    public static string For(DeviceInfoDto device) =>
        For(device.AppName, device.Name, device.Capabilities?.IconUrl ?? device.IconUrl);

    /// <param name="appName">The client app ("Jellyfin Web", "Kodi", ...).</param>
    /// <param name="deviceName">The device; for the web client it is the browser's name.</param>
    /// <param name="iconUrl">The icon the client announced in its capabilities.</param>
    public static string For(string? appName, string? deviceName, string? iconUrl)
    {
        var file = appName switch
        {
            "Samsung Smart TV" => "samsungtv",
            "Xbox One" => "xbox",
            "Sony PS4" => "playstation",
            "Kodi" or "Kodi JellyCon" => "kodi",
            "Jellyfin Android" or "AndroidTV" or "Android TV" or "Jellyfin Android TV" or "Jellyfin for Android"
                or "Jellyfin for Android TV" => "android",
            "Jellyfin Mobile (iOS)" or "Jellyfin Mobile (iPadOS)" or "Jellyfin iOS" or "Jellyfin iPadOS" or "Jellyfin tvOS"
                or "Swiftfin iPadOS" or "Swiftfin iOS" or "Swiftfin tvOS" or "Infuse" or "Infuse-Direct" or "Infuse-Library" => "apple",
            "Home Assistant" => "home-assistant",
            "Jellyfin for WebOS" or "LG Smart TV" => "webos",
            "Jellyfin Roku" => "roku",
            "Jellyfin for Titan OS" => "titanos",
            "Finamp" => "finamp",
            "Jellyfin Web" => deviceName switch
            {
                "Opera" or "Opera TV" or "Opera Android" => "opera",
                "Chrome" or "Chrome Android" => "chrome",
                "Firefox" or "Firefox Android" => "firefox",
                "Safari" or "Safari iPad" or "Safari iPhone" => "safari",
                "Edge Chromium" or "Edge Chromium Android" or "Edge Chromium iPad" or "Edge Chromium iPhone" => "edgechromium",
                "Edge" => "edge",
                "Internet Explorer" => "msie",
                "Titan OS" => "titanos",
                "Vega OS" => "firetv",
                _ => "html5",
            },
            _ => null,
        };

        if (file is not null)
        {
            return Base + file + ".svg";
        }

        return Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? iconUrl!
            : Base + "other.svg";
    }
}
