namespace Jellyfin.Web.Routing;

/// <summary>Who may open a page. Mirrors the access levels of the original ConnectionRequired.</summary>
public enum AccessLevel
{
    /// <summary>A signed-in user (the default for pages without an <see cref="AccessAttribute"/>).</summary>
    User,

    /// <summary>No sign-in needed: add/select server, login, password reset.</summary>
    Public,

    /// <summary>A signed-in administrator: dashboard and metadata manager.</summary>
    Admin,

    /// <summary>Only while the server's startup wizard is incomplete.</summary>
    Wizard
}

/// <summary>Declares the access level of a routable page: <c>@attribute [Access(AccessLevel.Admin)]</c>.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AccessAttribute(AccessLevel level) : Attribute
{
    public AccessLevel Level { get; } = level;
}

/// <summary>
/// By default a page component is recreated when the path or any query value other than <c>tab</c>
/// changes, so pages can load their data once in <c>OnInitializedAsync</c>. Use this attribute to
/// name further query keys whose change should update parameters in place instead.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class KeepOnQueryChangeAttribute(params string[] keys) : Attribute
{
    public IReadOnlyList<string> Keys { get; } = keys;
}
