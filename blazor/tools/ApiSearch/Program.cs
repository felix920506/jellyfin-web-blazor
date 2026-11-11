// Prints public API signatures from the referenced libraries so you can look up the exact
// shape of a generated SDK type without guessing.
//
//   ./dev api <type-regex> [member-regex]
//   ./dev api 'Users\.AuthenticateByName'         request builders under a path
//   ./dev api '^Jellyfin\.Sdk\.Generated\.Models\.BaseItemDto$' 'Image|Name'
using System.Reflection;
using System.Text.RegularExpressions;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: api <type-regex> [member-regex]");
    return 1;
}

var typeRx = new Regex(args[0], RegexOptions.IgnoreCase);
var memberRx = args.Length > 1 ? new Regex(args[1], RegexOptions.IgnoreCase) : null;
var assemblies = new[] { typeof(Jellyfin.Sdk.JellyfinApiClient).Assembly, typeof(MudBlazor.MudButton).Assembly };
var matched = 0;

foreach (var type in assemblies.SelectMany(a => a.GetExportedTypes()).OrderBy(t => t.FullName))
{
    if (!typeRx.IsMatch(type.FullName ?? type.Name))
    {
        continue;
    }

    matched++;
    if (matched > 60)
    {
        Console.WriteLine("... more than 60 types matched; narrow the type regex.");
        break;
    }

    Console.WriteLine($"{(type.IsEnum ? "enum" : type.IsInterface ? "interface" : "class")} {Name(type)}");
    if (type.IsEnum)
    {
        Console.WriteLine("    " + string.Join(", ", Enum.GetNames(type)));
        continue;
    }

    const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(Flags))
    {
        Print($"    ctor({Params(ctor)})");
    }

    foreach (var prop in type.GetProperties(Flags))
    {
        Print($"    {Name(prop.PropertyType)} {prop.Name} {{ {(prop.CanRead ? "get; " : string.Empty)}{(prop.CanWrite ? "set; " : string.Empty)}}}");
    }

    foreach (var method in type.GetMethods(Flags).Where(m => !m.IsSpecialName))
    {
        Print($"    {Name(method.ReturnType)} {method.Name}({Params(method)})");
    }
}

if (matched == 0)
{
    Console.Error.WriteLine("no type matched");
    return 2;
}

return 0;

void Print(string line)
{
    if (memberRx is null || memberRx.IsMatch(line))
    {
        Console.WriteLine(line);
    }
}

static string Params(MethodBase method) =>
    string.Join(", ", method.GetParameters().Select(p => $"{Name(p.ParameterType)} {p.Name}"));

static string Name(Type type)
{
    if (Nullable.GetUnderlyingType(type) is { } inner)
    {
        return Name(inner) + "?";
    }

    if (!type.IsGenericType)
    {
        return type.IsNested && !type.IsGenericParameter ? $"{Name(type.DeclaringType!)}.{type.Name}" : type.Name;
    }

    var name = type.Name[..type.Name.IndexOf('`')];
    return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
}
