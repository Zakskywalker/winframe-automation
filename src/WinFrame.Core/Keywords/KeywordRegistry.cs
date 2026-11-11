using System.Globalization;
using System.Reflection;
using System.Windows.Automation;
using WinFrame.Core.Logging;

namespace WinFrame.Core.Keywords;

/// <summary>Metadata and reflection descriptor for a registered keyword command.</summary>
public sealed record KeywordDescriptor(
    string Name,
    string Category,
    string Description,
    MethodInfo Method)
{
    public string Signature =>
        $"{Name}({string.Join(", ", Method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})";
}

/// <summary>
/// Discovers and invokes all <see cref="KeywordAttribute"/>-decorated static methods across
/// <c>Win32Automation</c>, <c>UiaAutomation</c>, <c>OpenCvAutomation</c>, <c>ProcessManagementEngine</c>,
/// built-in utility keywords, and any user assemblies registered via <see cref="RegisterAssembly"/>.
/// </summary>
public static class KeywordRegistry
{
    private static readonly Dictionary<string, KeywordDescriptor> map = new(StringComparer.OrdinalIgnoreCase);
    private static bool initialized;

    public static IReadOnlyCollection<KeywordDescriptor> All
    {
        get { EnsureInitialized(); return map.Values.OrderBy(k => k.Category).ThenBy(k => k.Name).ToList(); }
    }

    public static void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        RegisterAssembly(typeof(KeywordRegistry).Assembly);
    }

    public static void RegisterAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            var libAttr = type.GetCustomAttribute<KeywordLibraryAttribute>();
            if (libAttr != null) RegisterType(type, libAttr.Category);
        }
    }

    public static void RegisterType(Type type, string category = "Custom")
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var attrs = method.GetCustomAttributes<KeywordAttribute>().ToList();
            foreach (var attr in attrs)
            {
                map[attr.Name] = new KeywordDescriptor(attr.Name, category, attr.Description, method);
            }
        }
    }

    public static bool TryGet(string actionName, out KeywordDescriptor descriptor)
    {
        EnsureInitialized();
        return map.TryGetValue(actionName.Trim(), out descriptor!);
    }

    /// <summary>
    /// Invokes a keyword with automatic parameter binding from <see cref="KeywordContext"/>,
    /// Target (<see cref="IntPtr"/>, <see cref="AutomationElement"/>, or <c>string</c>), and pipe-delimited Value tokens.
    /// </summary>
    public static object? Invoke(KeywordDescriptor descriptor, KeywordContext ctx, string rawTarget, string rawValue, int? stepTimeoutMs = null)
    {
        string target = ctx.Expand(rawTarget);
        string value = ctx.Expand(rawValue);
        string[] valueTokens = string.IsNullOrEmpty(value) ? Array.Empty<string>() : value.Split('|');
        int tokenIdx = 0;

        var parameters = descriptor.Method.GetParameters();
        var args = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var pt = p.ParameterType;

            if (pt == typeof(KeywordContext))
            {
                args[i] = ctx;
            }
            else if (pt == typeof(IntPtr))
            {
                args[i] = ctx.ResolveHwnd(target, stepTimeoutMs ?? ctx.Config.DefaultTimeoutMs);
            }
            else if (pt == typeof(AutomationElement))
            {
                args[i] = ctx.ResolveElement(target, stepTimeoutMs ?? ctx.Config.DefaultTimeoutMs);
            }
            else if (pt == typeof(string) && p.Name != null &&
                     (p.Name.Equals("target", StringComparison.OrdinalIgnoreCase) ||
                      p.Name.Equals("rectSpec", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(target)))
            {
                args[i] = !string.IsNullOrWhiteSpace(target) ? target : NextToken(valueTokens, ref tokenIdx, p);
            }
            else
            {
                // If only one remaining string parameter and it's the first value token, pass the whole value if not pipe-split
                string rawArg;
                if (pt == typeof(string) && tokenIdx == 0 && CountValueParams(parameters, i) == 1)
                {
                    rawArg = value;
                    tokenIdx = valueTokens.Length;
                }
                else
                {
                    rawArg = NextToken(valueTokens, ref tokenIdx, p);
                }
                args[i] = ConvertValue(rawArg, pt, p);
            }
        }

        try
        {
            return descriptor.Method.Invoke(null, args);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            throw tie.InnerException;
        }
    }

    private static int CountValueParams(ParameterInfo[] parameters, int startIndex)
    {
        int count = 0;
        for (int i = startIndex; i < parameters.Length; i++)
        {
            var pt = parameters[i].ParameterType;
            if (pt != typeof(KeywordContext) && pt != typeof(IntPtr) && pt != typeof(AutomationElement))
                count++;
        }
        return count;
    }

    private static string NextToken(string[] tokens, ref int idx, ParameterInfo p)
    {
        if (idx < tokens.Length) return tokens[idx++];
        return p.HasDefaultValue ? p.DefaultValue?.ToString() ?? "" : "";
    }

    private static object? ConvertValue(string text, Type targetType, ParameterInfo p)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying == typeof(string)) return text;
        if (string.IsNullOrWhiteSpace(text) && p.HasDefaultValue) return p.DefaultValue;
        if (underlying == typeof(int)) return int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out int iv) ? iv : 0;
        if (underlying == typeof(double)) return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double dv) ? dv : 0.0;
        if (underlying == typeof(bool)) return bool.TryParse(text, out bool bv) ? bv : text == "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase);
        if (underlying.IsEnum) return Enum.Parse(underlying, text, ignoreCase: true);
        return Convert.ChangeType(text, underlying, CultureInfo.InvariantCulture);
    }
}

/// <summary>Built-in flow control, variable, and utility keywords.</summary>
[KeywordLibrary("Flow & Utilities")]
public static class BuiltInKeywords
{
    [Keyword("WAIT", Description = "Pauses execution for Value milliseconds.")]
    [Keyword("SLEEP")]
    public static void Wait(int milliseconds = 500) => Thread.Sleep(Math.Max(0, milliseconds));

    [Keyword("LOG", Description = "Writes a message to the Test log channel.")]
    public static void Log(string target, string value = "") =>
        SystemLogger.Test(string.IsNullOrWhiteSpace(value) ? target : $"{target}: {value}");

    [Keyword("SET_VAR", Description = "Sets variable named Target to Value.")]
    public static string SetVar(KeywordContext ctx, string target, string value)
    {
        ctx.Variables[target] = value;
        return value;
    }

    [Keyword("ASSERT_VAR", Description = "Verifies that variable named Target equals Expected.")]
    [Keyword("CHECK_VAR")]
    public static bool AssertVar(KeywordContext ctx, string target, string expected) =>
        ctx.Variables.TryGetValue(target, out var actual) &&
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
