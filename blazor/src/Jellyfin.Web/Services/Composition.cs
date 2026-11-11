using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Services;

/// <summary>
/// Registers the class as a singleton service without touching the central registration list.
/// With a <paramref name="serviceType"/> the class is also resolvable as that type and replaces
/// any earlier registration of it (this is how the real playback manager replaces the null one).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AppServiceAttribute(Type? serviceType = null) : Attribute
{
    public Type? ServiceType { get; } = serviceType;
}

/// <summary>
/// Implemented by an <see cref="AppServiceAttribute"/> service that must run once at startup,
/// after configuration, connection and strings are ready and before the first page renders
/// (subscribe to server events or to sign-in / sign-out here).
/// </summary>
public interface IStartupTask
{
    Task StartAsync();
}

/// <summary>Places in the app shell where features mount a component of their own.</summary>
public enum ShellSlot
{
    /// <summary>App bar buttons, before the search button.</summary>
    Toolbar,

    /// <summary>Fixed to the bottom of the layout, below the page (now-playing bar).</summary>
    Footer,

    /// <summary>Rendered once at the root, above every page (players, global listeners).</summary>
    Overlay,
}

/// <summary>Mounts the component in a shell slot. The component takes no parameters.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ShellSlotAttribute(ShellSlot slot, int order = 0) : Attribute
{
    public ShellSlot Slot { get; } = slot;

    public int Order { get; } = order;
}

/// <summary>The components registered for each shell slot.</summary>
public sealed class ShellSlots
{
    private readonly Dictionary<ShellSlot, List<Type>> _components;

    public ShellSlots(Assembly assembly)
    {
        _components = assembly.GetTypes()
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<ShellSlotAttribute>()))
            .Where(entry => entry.Attribute is not null)
            .GroupBy(entry => entry.Attribute!.Slot)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(e => e.Attribute!.Order).ThenBy(e => e.Type.FullName, StringComparer.Ordinal).Select(e => e.Type).ToList());
    }

    public IReadOnlyList<Type> For(ShellSlot slot) => _components.TryGetValue(slot, out var types) ? types : [];
}

internal static class Composition
{
    /// <summary>Registers every <see cref="AppServiceAttribute"/> class of the assembly.</summary>
    public static void AddAttributedServices(IServiceCollection services, Assembly assembly)
    {
        var attributed = assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsDefined(typeof(AppServiceAttribute), false))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
        foreach (var type in attributed)
        {
            services.AddSingleton(type);
            foreach (var attribute in type.GetCustomAttributes<AppServiceAttribute>())
            {
                if (attribute.ServiceType is { } serviceType)
                {
                    services.AddSingleton(serviceType, sp => sp.GetRequiredService(type));
                }
            }

            if (typeof(IStartupTask).IsAssignableFrom(type))
            {
                services.AddSingleton(typeof(IStartupTask), sp => sp.GetRequiredService(type));
            }
        }

        services.AddSingleton(new ShellSlots(assembly));
    }
}
