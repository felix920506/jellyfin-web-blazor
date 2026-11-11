namespace Jellyfin.Web.Components;

/// <summary>Generates ids for form elements that need a label/for pair.</summary>
internal static class ElementIds
{
    private static int _next;

    public static string Next(string prefix) => prefix + Interlocked.Increment(ref _next);
}
