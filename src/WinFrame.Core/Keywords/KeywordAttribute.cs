namespace WinFrame.Core.Keywords;

/// <summary>
/// Marks a public static method as callable from a keyword sheet. A method may expose several names
/// (aliases). Parameters are bound automatically by <see cref="KeywordRegistry"/>:
/// <list type="bullet">
/// <item><see cref="KeywordContext"/> – the current execution context.</item>
/// <item><see cref="IntPtr"/> – the step Target resolved to a window/control handle.</item>
/// <item><c>System.Windows.Automation.AutomationElement</c> – the step Target resolved via UI Automation.</item>
/// <item><c>string target</c> – the raw (variable-substituted) Target text.</item>
/// <item>any other parameter – taken in order from the Value column (split on '|') and converted.</item>
/// </list>
/// Methods returning <c>bool</c> are treated as checks: <c>false</c> fails the step.
/// Other return values are stored in <see cref="KeywordContext.LastResult"/> and, when the step has
/// a <c>store=varName</c> option, in the variable table.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class KeywordAttribute : Attribute
{
    public KeywordAttribute(string name) => Name = name.ToUpperInvariant();
    public string Name { get; }
    public string Description { get; set; } = "";
    public string Example { get; set; } = "";
}

/// <summary>Marks a class whose static <see cref="KeywordAttribute"/> methods should be auto-registered.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class KeywordLibraryAttribute : Attribute
{
    public KeywordLibraryAttribute(string category) => Category = category;
    public string Category { get; }
}

