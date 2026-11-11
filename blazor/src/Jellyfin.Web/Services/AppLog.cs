namespace Jellyfin.Web.Services;

/// <summary>
/// Diagnostics for recoverable failures. Never write to <c>Console.Error</c>: in Blazor WebAssembly
/// anything on stderr raises the "An unhandled error has occurred" banner.
/// </summary>
public static class AppLog
{
    public static void Warn(string message) => Console.WriteLine("[warn] " + message);
}
