using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>One trickplay resolution of a media source: tile sheets of preview images.</summary>
public sealed record TrickplayResolution(int Width, int Height, int TileWidth, int TileHeight, int Interval);

/// <summary>The preview image of a position: the sheet's index and the tile's offset in it.</summary>
public readonly record struct TrickplayTile(int SheetIndex, int OffsetX, int OffsetY);

/// <summary>
/// Trickplay (seek preview) lookups of the video OSD. Port of the trickplay parts of
/// controllers/playback/video/index.js.
/// </summary>
public static class Trickplay
{
    /// <summary>
    /// The resolution to use for a media source: the widest one that is not wider than
    /// <paramref name="maxWidth"/>, or the narrowest when all are wider.
    /// </summary>
    public static TrickplayResolution? Select(BaseItemDto? item, string? mediaSourceId, double maxWidth)
    {
        if (item?.Trickplay is not { } trickplay || string.IsNullOrEmpty(mediaSourceId))
        {
            return null;
        }

        // The SDK keeps the dictionary as untyped data; its JSON form is the server's.
        JsonObject? resolutions = null;
        try
        {
            if (JsonNode.Parse(KiotaJson.Serialize(trickplay)) is JsonObject root)
            {
                var wanted = NormalizeId(mediaSourceId);
                resolutions = root.FirstOrDefault(pair => NormalizeId(pair.Key) == wanted).Value as JsonObject;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (resolutions is null)
        {
            return null;
        }

        TrickplayResolution? best = null;
        foreach (var (_, node) in resolutions)
        {
            if (node is not JsonObject info || Int(info, "Width") is not { } width || width <= 0)
            {
                continue;
            }

            if (best is null || (width < best.Width && best.Width > maxWidth) || (width > best.Width && width <= maxWidth))
            {
                best = new TrickplayResolution(
                    width,
                    Int(info, "Height") ?? 0,
                    Int(info, "TileWidth") ?? 0,
                    Int(info, "TileHeight") ?? 0,
                    Int(info, "Interval") ?? 0);
            }
        }

        return best is { Height: > 0, TileWidth: > 0, TileHeight: > 0, Interval: > 0 } ? best : null;
    }

    /// <summary>The tile that shows <paramref name="positionTicks"/>.</summary>
    public static TrickplayTile Tile(TrickplayResolution info, long positionTicks)
    {
        var currentTile = (int)Math.Floor(positionTicks / 10000d / info.Interval);
        var tileSize = info.TileWidth * info.TileHeight;
        var tileOffset = currentTile % tileSize;
        var sheetIndex = currentTile / tileSize;
        var tileOffsetX = tileOffset % info.TileWidth;
        var tileOffsetY = tileOffset / info.TileWidth;
        return new TrickplayTile(sheetIndex, -(tileOffsetX * info.Width), -(tileOffsetY * info.Height));
    }

    private static string NormalizeId(string id) => id.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static int? Int(JsonObject info, string name)
    {
        if (info[name] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<double>(out var real) ? (int)real : null;
    }
}
