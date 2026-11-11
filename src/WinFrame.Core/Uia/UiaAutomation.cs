using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using Condition = System.Windows.Automation.Condition;
using WinFrame.Core.Controls;
using WinFrame.Core.Keywords;
using WinFrame.Core.Logging;
using WinFrame.Core.Native;
using WinFrame.Core.Processes;

namespace WinFrame.Core.Uia;

/// <summary>
/// Microsoft UI Automation (UIA) Engine.
/// Provides locator resolution, pattern invocation (Invoke, Value, Toggle, ExpandCollapse, SelectionItem,
/// RangeValue, Scroll, Text, Window), tree harvesting with full attribute extraction, and assertion checks.
/// Every function and check is decorated with <see cref="KeywordAttribute"/> so it can be called directly
/// from any keyword sheet just like the native Win32 methods.
/// </summary>
[KeywordLibrary("UI Automation (UIA)")]
public static class UiaAutomation
{
    // --------------------------------------------------------------------------------------------
    // Element resolution
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a target string to an <see cref="AutomationElement"/>:
    /// <list type="bullet">
    /// <item><c>@controlName</c> or a name matching an entry in <see cref="ControlRepository"/>.</item>
    /// <item>Hex/decimal HWND (<c>0x1234</c> or integer).</item>
    /// <item>Key-value locator (<c>id=num8Button;name=Eight;type=Button;class=Button;window=Calculator;path=0/2/4</c>).</item>
    /// <item>Plain string: matched against AutomationId first, then Name.</item>
    /// </list>
    /// </summary>
    public static AutomationElement? FindElement(
        string target,
        IntPtr rootHwnd = default,
        ControlRepository? repo = null,
        int timeoutMs = 5000)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return rootHwnd != IntPtr.Zero ? TryFromHandle(rootHwnd) : TryFromHandle(NativeMethods.GetForegroundWindow());
        }

        // 1. Check ControlRepository if target is a known control name
        if (repo != null && repo.TryGet(target, out var def))
        {
            var locator = def.ToLocator();
            if (!string.IsNullOrWhiteSpace(locator))
            {
                var found = FindByLocator(locator, rootHwnd, timeoutMs);
                if (found != null) return found;
            }
            var hwndStr = def.Get(ControlDefinition.Keys.NativeWindowHandle);
            if (TryParseHwnd(hwndStr, out IntPtr defHwnd) && NativeMethods.IsWindow(defHwnd))
            {
                return TryFromHandle(defHwnd);
            }
        }

        // 2. Direct HWND?
        if (TryParseHwnd(target, out IntPtr hwnd) && hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd))
        {
            return TryFromHandle(hwnd);
        }

        // 3. Locator string or plain id/name
        return FindByLocator(target, rootHwnd, timeoutMs);
    }

    public static AutomationElement? TryFromHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return null;
        try { return AutomationElement.FromHandle(hwnd); }
        catch { return null; }
    }

    public static AutomationElement? ElementFromPoint(int x, int y)
    {
        try { return AutomationElement.FromPoint(new System.Windows.Point(x, y)); }
        catch { return null; }
    }

    public static bool TryParseHwnd(string? text, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out long hex))
        {
            hwnd = new IntPtr(hex);
            return true;
        }
        if (long.TryParse(text, out long dec) && dec > 100)
        {
            hwnd = new IntPtr(dec);
            return true;
        }
        return false;
    }

    private static AutomationElement? FindByLocator(string locator, IntPtr rootHwnd, int timeoutMs)
    {
        var clauses = ParseLocator(locator);
        string? windowFilter = clauses.GetValueOrDefault("window");
        string? treePath = clauses.GetValueOrDefault("path");

        var sw = Stopwatch.StartNew();
        do
        {
            AutomationElement root = ResolveRoot(rootHwnd, windowFilter);

            if (!string.IsNullOrWhiteSpace(treePath) && clauses.Count == (windowFilter != null ? 2 : 1))
            {
                var byPath = FollowTreePath(root, treePath);
                if (byPath != null) return byPath;
            }

            Condition cond = BuildCondition(clauses);
            try
            {
                var el = root.FindFirst(TreeScope.Descendants, cond);
                if (el != null) return el;

                // Fallback: if plain string didn't match AutomationId, try Name
                if (clauses.TryGetValue("__plain", out var plain))
                {
                    var nameCond = new PropertyCondition(AutomationElement.NameProperty, plain, PropertyConditionFlags.IgnoreCase);
                    el = root.FindFirst(TreeScope.Descendants, nameCond);
                    if (el != null) return el;
                }

                if (!string.IsNullOrWhiteSpace(treePath))
                {
                    var byPath = FollowTreePath(root, treePath);
                    if (byPath != null) return byPath;
                }
            }
            catch { }

            Thread.Sleep(150);
        } while (sw.ElapsedMilliseconds < timeoutMs);

        return null;
    }

    private static AutomationElement ResolveRoot(IntPtr rootHwnd, string? windowFilter)
    {
        if (!string.IsNullOrWhiteSpace(windowFilter))
        {
            var winHwnd = ProcessManagementEngine.FindTopLevelWindow(windowFilter);
            if (winHwnd != IntPtr.Zero)
            {
                var winEl = TryFromHandle(winHwnd);
                if (winEl != null) return winEl;
            }
        }
        if (rootHwnd != IntPtr.Zero && NativeMethods.IsWindow(rootHwnd))
        {
            var el = TryFromHandle(rootHwnd);
            if (el != null) return el;
        }
        var active = ProcessManagementEngine.Shared.Active;
        if (active != null && active.MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindow(active.MainWindowHandle))
        {
            var el = TryFromHandle(active.MainWindowHandle);
            if (el != null) return el;
        }
        return AutomationElement.RootElement;
    }

    private static Dictionary<string, string> ParseLocator(string locator)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!locator.Contains('='))
        {
            d["__plain"] = locator.Trim();
            return d;
        }
        foreach (var part in locator.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0) d[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        return d;
    }

    private static Condition BuildCondition(Dictionary<string, string> clauses)
    {
        if (clauses.TryGetValue("__plain", out var plain))
        {
            return new PropertyCondition(AutomationElement.AutomationIdProperty, plain);
        }
        var list = new List<Condition>();
        if (clauses.TryGetValue("id", out var id) && !string.IsNullOrEmpty(id))
            list.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        if (clauses.TryGetValue("name", out var name) && !string.IsNullOrEmpty(name))
            list.Add(new PropertyCondition(AutomationElement.NameProperty, name, PropertyConditionFlags.IgnoreCase));
        if (clauses.TryGetValue("class", out var cls) && !string.IsNullOrEmpty(cls))
            list.Add(new PropertyCondition(AutomationElement.ClassNameProperty, cls));
        if (clauses.TryGetValue("type", out var type) && TryGetControlType(type, out var ct))
            list.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, ct));

        return list.Count switch
        {
            0 => Condition.TrueCondition,
            1 => list[0],
            _ => new AndCondition(list.ToArray())
        };
    }

    private static bool TryGetControlType(string name, out ControlType ct)
    {
        ct = ControlType.Custom;
        name = name.Replace("ControlType.", "", StringComparison.OrdinalIgnoreCase).Trim();
        var field = typeof(ControlType).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (field?.GetValue(null) is ControlType val) { ct = val; return true; }
        return false;
    }

    private static AutomationElement? FollowTreePath(AutomationElement root, string path)
    {
        AutomationElement? cur = root;
        foreach (var seg in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (cur == null || !int.TryParse(seg, out int idx)) return null;
            var children = cur.FindAll(TreeScope.Children, Condition.TrueCondition);
            if (idx < 0 || idx >= children.Count) return null;
            cur = children[idx];
        }
        return cur;
    }

    // --------------------------------------------------------------------------------------------
    // Harvesting & Attribute extraction
    // --------------------------------------------------------------------------------------------

    /// <summary>Extracts every possible UIA + Win32 attribute for an element into a dictionary.</summary>
    public static Dictionary<string, string> ExtractAttributes(AutomationElement el, string treePath = "", int index = 0, string? windowTitle = null, Rect? windowBounds = null)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Set(string k, Func<object?> getter)
        {
            try
            {
                var v = getter();
                if (v != null) d[k] = v.ToString() ?? "";
            }
            catch { }
        }

        var cur = el.Current;
        Set(ControlDefinition.Keys.Index, () => index);
        Set(ControlDefinition.Keys.TreePath, () => treePath);
        Set(ControlDefinition.Keys.AutomationId, () => cur.AutomationId);
        Set(ControlDefinition.Keys.Name, () => cur.Name);
        Set(ControlDefinition.Keys.ControlType, () => cur.ControlType?.ProgrammaticName.Replace("ControlType.", ""));
        Set(ControlDefinition.Keys.LocalizedControlType, () => cur.LocalizedControlType);
        Set(ControlDefinition.Keys.ClassName, () => cur.ClassName);
        Set(ControlDefinition.Keys.FrameworkId, () => cur.FrameworkId);
        Set("ProcessId", () => cur.ProcessId);
        Set("RuntimeId", () => string.Join(".", el.GetRuntimeId()));
        Set("IsEnabled", () => cur.IsEnabled);
        Set("IsOffscreen", () => cur.IsOffscreen);
        Set("IsKeyboardFocusable", () => cur.IsKeyboardFocusable);
        Set("HasKeyboardFocus", () => cur.HasKeyboardFocus);
        Set("IsPassword", () => cur.IsPassword);
        Set("AcceleratorKey", () => cur.AcceleratorKey);
        Set("AccessKey", () => cur.AccessKey);
        Set("HelpText", () => cur.HelpText);
        Set("ItemStatus", () => cur.ItemStatus);
        Set("ItemType", () => cur.ItemType);

        try
        {
            using var p = Process.GetProcessById(cur.ProcessId);
            d[ControlDefinition.Keys.ProcessName] = p.ProcessName;
        }
        catch { }

        if (!string.IsNullOrEmpty(windowTitle)) d[ControlDefinition.Keys.WindowTitle] = windowTitle;

        try
        {
            var r = cur.BoundingRectangle;
            if (!r.IsEmpty && !double.IsInfinity(r.Width))
            {
                d[ControlDefinition.Keys.BoundingRectangle] = $"{(int)r.Left},{(int)r.Top},{(int)r.Width},{(int)r.Height}";
                if (windowBounds is { IsEmpty: false } wb)
                {
                    d[ControlDefinition.Keys.RelativeBounds] = $"{(int)(r.Left - wb.Left)},{(int)(r.Top - wb.Top)},{(int)r.Width},{(int)r.Height}";
                }
            }
        }
        catch { }

        try
        {
            if (cur.NativeWindowHandle != 0)
            {
                var h = new IntPtr(cur.NativeWindowHandle);
                d[ControlDefinition.Keys.NativeWindowHandle] = $"0x{h.ToInt64():X}";
                d[ControlDefinition.Keys.Win32CtrlId] = NativeMethods.GetDlgCtrlID(h).ToString();
            }
        }
        catch { }

        try
        {
            var patterns = el.GetSupportedPatterns().Select(p => p.ProgrammaticName.Replace("PatternIdentifiers.Pattern", "")).ToArray();
            d[ControlDefinition.Keys.Patterns] = string.Join(", ", patterns);
        }
        catch { }

        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                d["Value"] = ((ValuePattern)vp).Current.Value ?? "";
        }
        catch { }

        try
        {
            if (el.TryGetCurrentPattern(TogglePattern.Pattern, out var tp))
                d["ToggleState"] = ((TogglePattern)tp).Current.ToggleState.ToString();
        }
        catch { }

        return d;
    }

    /// <summary>Walks the UIA descendant tree of a root window and builds a list of <see cref="ControlDefinition"/>s.</summary>
    public static List<ControlDefinition> ScrapeWindow(IntPtr windowHwnd, ControlRepository? repo = null, int maxDepth = 8, int maxControls = 400)
    {
        var results = new List<ControlDefinition>();
        var root = TryFromHandle(windowHwnd) ?? AutomationElement.RootElement;
        string winTitle = "";
        Rect? winRect = null;
        try { winTitle = root.Current.Name; winRect = root.Current.BoundingRectangle; } catch { }

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int counter = 0;

        void Walk(AutomationElement node, string path, int depth)
        {
            if (depth > maxDepth || counter >= maxControls) return;
            AutomationElementCollection children;
            try { children = node.FindAll(TreeScope.Children, Condition.TrueCondition); }
            catch { return; }

            for (int i = 0; i < children.Count && counter < maxControls; i++)
            {
                AutomationElement child = children[i];
                string childPath = string.IsNullOrEmpty(path) ? i.ToString() : $"{path}/{i}";
                try
                {
                    var attrs = ExtractAttributes(child, childPath, counter, winTitle, winRect);
                    string ctype = attrs.GetValueOrDefault(ControlDefinition.Keys.ControlType, "control");
                    var existing = repo?.FindMatch(attrs);
                    string name = existing?.Name ?? (repo != null
                        ? repo.GenerateDefaultName(ctype, reserved)
                        : $"{ctype.ToLowerInvariant()}_{counter + 1}");
                    reserved.Add(name);

                    results.Add(new ControlDefinition
                    {
                        Name = name,
                        Attributes = attrs
                    });
                    counter++;
                }
                catch { }
                Walk(child, childPath, depth + 1);
            }
        }

        Walk(root, "", 1);
        SystemLogger.Log($"UIA scraped {results.Count} controls from '{winTitle}' (HWND {windowHwnd}).");
        return results;
    }

    /// <summary>Gets the absolute center point of a UIA element (falling back to its native HWND if needed).</summary>
    public static NativeMethods.POINT GetCenterPoint(AutomationElement el)
    {
        try
        {
            if (el.TryGetClickablePoint(out var pt))
                return new NativeMethods.POINT((int)pt.X, (int)pt.Y);
        }
        catch { }
        var r = el.Current.BoundingRectangle;
        if (!r.IsEmpty && !double.IsInfinity(r.Width) && r.Width > 0)
            return new NativeMethods.POINT((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2));
        if (el.Current.NativeWindowHandle != 0)
            return ScreenLocationManager.GetControlCenterPoint(new IntPtr(el.Current.NativeWindowHandle));
        return new NativeMethods.POINT(0, 0);
    }

    /// <summary>Gets the screen bounding rectangle of a UIA element.</summary>
    public static System.Drawing.Rectangle GetBounds(AutomationElement el)
    {
        var r = el.Current.BoundingRectangle;
        if (!r.IsEmpty && !double.IsInfinity(r.Width) && r.Width > 0)
            return new System.Drawing.Rectangle((int)r.Left, (int)r.Top, (int)r.Width, (int)r.Height);
        if (el.Current.NativeWindowHandle != 0)
            return ScreenLocationManager.ToRectangle(ScreenLocationManager.GetAbsoluteControlBounds(new IntPtr(el.Current.NativeWindowHandle)));
        return System.Drawing.Rectangle.Empty;
    }

    // --------------------------------------------------------------------------------------------
    // Pattern Actions & Checks (All callable as Keywords)
    // --------------------------------------------------------------------------------------------

    [Keyword("UIA_INVOKE", Description = "Invokes Target UIA element via InvokePattern (falls back to native click).")]
    [Keyword("UIA_CLICK")]
    public static bool Invoke(AutomationElement el)
    {
        if (el == null) return false;
        try
        {
            if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var p))
            {
                ((InvokePattern)p).Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            SystemLogger.Log($"InvokePattern failed, falling back to click: {ex.Message}", SystemLogger.LogLevel.DEBUG);
        }
        var pt = GetCenterPoint(el);
        Win32Automation.ClickAtPoint(pt.X, pt.Y, "LEFT", 1);
        return true;
    }

    [Keyword("UIA_DOUBLE_CLICK", Description = "Double-clicks the clickable point of Target UIA element.")]
    public static bool DoubleClick(AutomationElement el)
    {
        if (el == null) return false;
        var pt = GetCenterPoint(el);
        Win32Automation.ClickAtPoint(pt.X, pt.Y, "LEFT", 2);
        return true;
    }

    [Keyword("UIA_RIGHT_CLICK", Description = "Right-clicks the clickable point of Target UIA element.")]
    public static bool RightClick(AutomationElement el)
    {
        if (el == null) return false;
        var pt = GetCenterPoint(el);
        Win32Automation.ClickAtPoint(pt.X, pt.Y, "RIGHT", 1);
        return true;
    }

    [Keyword("UIA_SET_VALUE", Description = "Sets Value on Target UIA element via ValuePattern or keyboard typing fallback.")]
    [Keyword("UIA_TYPE")]
    public static bool SetValue(AutomationElement el, string value)
    {
        if (el == null) return false;
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var p))
            {
                var vp = (ValuePattern)p;
                if (!vp.Current.IsReadOnly)
                {
                    vp.SetValue(value ?? "");
                    return true;
                }
            }
        }
        catch { }

        try { el.SetFocus(); } catch { }
        var pt = GetCenterPoint(el);
        if (pt.X != 0 || pt.Y != 0) Win32Automation.ClickAtPoint(pt.X, pt.Y, "LEFT", 1);
        Thread.Sleep(50);
        Win32Automation.TypeUnicodeText(value ?? "");
        return true;
    }

    [Keyword("UIA_CLEAR_TEXT", Description = "Clears text on Target UIA element (or restores Value if provided) - inverse of UIA_SET_VALUE/UIA_TYPE.")]
    [Keyword("UIA_DELETE_TEXT")]
    public static bool ClearValue(AutomationElement el, string restoreValue = "")
    {
        if (el == null) return false;
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var p))
            {
                var vp = (ValuePattern)p;
                if (!vp.Current.IsReadOnly)
                {
                    vp.SetValue(restoreValue ?? "");
                    return true;
                }
            }
        }
        catch { }
        try { el.SetFocus(); } catch { }
        var pt = GetCenterPoint(el);
        if (pt.X != 0 || pt.Y != 0) Win32Automation.ClickAtPoint(pt.X, pt.Y, "LEFT", 1);
        Thread.Sleep(40);
        System.Windows.Forms.SendKeys.SendWait("^a{BACKSPACE}");
        if (!string.IsNullOrEmpty(restoreValue)) Win32Automation.TypeUnicodeText(restoreValue);
        return true;
    }

    [Keyword("UIA_GET_VALUE", Description = "Reads value/text/name of Target UIA element.")]
    [Keyword("UIA_GET_TEXT")]
    public static string GetValue(AutomationElement el)
    {
        if (el == null) return "";
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                return ((ValuePattern)vp).Current.Value ?? "";
            if (el.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
                return ((TextPattern)tp).DocumentRange.GetText(-1)?.TrimEnd('\r', '\n') ?? "";
            if (el.TryGetCurrentPattern(RangeValuePattern.Pattern, out var rp))
                return ((RangeValuePattern)rp).Current.Value.ToString();
        }
        catch { }
        return el.Current.Name ?? "";
    }

    [Keyword("UIA_TOGGLE", Description = "Toggles Target checkbox/toggle button (optional Value: On, Off, Indeterminate).")]
    public static bool Toggle(AutomationElement el, string desiredState = "")
    {
        if (el == null) return false;
        if (el.TryGetCurrentPattern(TogglePattern.Pattern, out var p))
        {
            var tp = (TogglePattern)p;
            if (string.IsNullOrWhiteSpace(desiredState))
            {
                tp.Toggle();
                return true;
            }
            for (int i = 0; i < 3; i++)
            {
                if (tp.Current.ToggleState.ToString().Equals(desiredState, StringComparison.OrdinalIgnoreCase))
                    return true;
                tp.Toggle();
                Thread.Sleep(60);
            }
            return tp.Current.ToggleState.ToString().Equals(desiredState, StringComparison.OrdinalIgnoreCase);
        }
        return Invoke(el);
    }

    [Keyword("UIA_SELECT", Description = "Selects Target item or child item matching Value via SelectionItemPattern.")]
    public static bool Select(AutomationElement el, string itemName = "")
    {
        if (el == null) return false;
        AutomationElement? targetItem = el;
        if (!string.IsNullOrWhiteSpace(itemName))
        {
            Expand(el);
            Thread.Sleep(100);
            var cond = new PropertyCondition(AutomationElement.NameProperty, itemName, PropertyConditionFlags.IgnoreCase);
            targetItem = el.FindFirst(TreeScope.Descendants, cond) ?? el;
        }
        if (targetItem.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p))
        {
            ((SelectionItemPattern)p).Select();
            return true;
        }
        return Invoke(targetItem);
    }

    [Keyword("UIA_EXPAND", Description = "Expands a ComboBox, TreeItem or Menu via ExpandCollapsePattern.")]
    public static bool Expand(AutomationElement el)
    {
        if (el == null) return false;
        if (el.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p))
        {
            ((ExpandCollapsePattern)p).Expand();
            return true;
        }
        return false;
    }

    [Keyword("UIA_COLLAPSE", Description = "Collapses a ComboBox, TreeItem or Menu via ExpandCollapsePattern.")]
    public static bool Collapse(AutomationElement el)
    {
        if (el == null) return false;
        if (el.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p))
        {
            ((ExpandCollapsePattern)p).Collapse();
            return true;
        }
        return false;
    }

    [Keyword("UIA_SET_RANGE", Description = "Sets numeric Value on a Slider/ProgressBar/Spinner via RangeValuePattern.")]
    public static bool SetRangeValue(AutomationElement el, double value)
    {
        if (el == null) return false;
        if (el.TryGetCurrentPattern(RangeValuePattern.Pattern, out var p))
        {
            ((RangeValuePattern)p).SetValue(value);
            return true;
        }
        return false;
    }

    [Keyword("UIA_SCROLL", Description = "Scrolls Target container or brings Target into view (ScrollItemPattern).")]
    public static bool Scroll(AutomationElement el, double horizontalPercent = -1, double verticalPercent = 50)
    {
        if (el == null) return false;
        if (el.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var sip))
        {
            ((ScrollItemPattern)sip).ScrollIntoView();
            return true;
        }
        if (el.TryGetCurrentPattern(ScrollPattern.Pattern, out var sp))
        {
            ((ScrollPattern)sp).SetScrollPercent(
                horizontalPercent < 0 ? ScrollPattern.NoScroll : horizontalPercent,
                verticalPercent < 0 ? ScrollPattern.NoScroll : verticalPercent);
            return true;
        }
        return false;
    }

    [Keyword("UIA_FOCUS", Description = "Sets keyboard focus on Target UIA element.")]
    public static bool Focus(AutomationElement el)
    {
        if (el == null) return false;
        try { el.SetFocus(); return true; }
        catch { return false; }
    }

    [Keyword("UIA_WINDOW_STATE", Description = "Sets WindowVisualState (Normal, Maximized, Minimized) or Close on Target window.")]
    public static bool SetWindowState(AutomationElement el, string state = "Normal")
    {
        if (el == null) return false;
        if (el.TryGetCurrentPattern(WindowPattern.Pattern, out var p))
        {
            var wp = (WindowPattern)p;
            if (state.Equals("CLOSE", StringComparison.OrdinalIgnoreCase)) { wp.Close(); return true; }
            var vs = state.ToUpperInvariant() switch
            {
                "MAX" or "MAXIMIZED" or "MAXIMIZE" => WindowVisualState.Maximized,
                "MIN" or "MINIMIZED" or "MINIMIZE" => WindowVisualState.Minimized,
                _ => WindowVisualState.Normal
            };
            wp.SetWindowVisualState(vs);
            return true;
        }
        return false;
    }

    [Keyword("UIA_HARVEST", Description = "Scrapes all UIA controls in Target window into the ControlRepository.")]
    public static int KeywordHarvest(KeywordContext ctx, IntPtr hwnd)
    {
        var list = ScrapeWindow(hwnd != IntPtr.Zero ? hwnd : ctx.CurrentWindowHwnd, ctx.Controls);
        foreach (var c in list) ctx.Controls.AddOrUpdate(c);
        ctx.Controls.Save();
        return list.Count;
    }

    // --------------------------------------------------------------------------------------------
    // UIA Checks & Assertions
    // --------------------------------------------------------------------------------------------

    [Keyword("UIA_EXISTS", Description = "Checks that Target UIA element exists.")]
    [Keyword("UIA_CHECK_EXISTS")]
    public static bool CheckExists(AutomationElement? el) => el != null;

    [Keyword("UIA_NOT_EXISTS", Description = "Checks that Target UIA element does NOT exist within 1s.")]
    public static bool CheckNotExists(KeywordContext ctx, string target) =>
        FindElement(target, ctx.CurrentWindowHwnd, ctx.Controls, timeoutMs: 800) == null;

    [Keyword("UIA_CHECK_ENABLED", Description = "Checks that Target UIA element IsEnabled == expected (default true).")]
    public static bool CheckEnabled(AutomationElement? el, bool expected = true) =>
        el != null && el.Current.IsEnabled == expected;

    [Keyword("UIA_CHECK_VISIBLE", Description = "Checks that Target UIA element is on-screen (IsOffscreen == false).")]
    public static bool CheckVisible(AutomationElement? el, bool expected = true) =>
        el != null && (!el.Current.IsOffscreen) == expected;

    [Keyword("UIA_CHECK_FOCUSED", Description = "Checks that Target UIA element has keyboard focus.")]
    public static bool CheckFocused(AutomationElement? el) =>
        el != null && el.Current.HasKeyboardFocus;

    [Keyword("UIA_VERIFY", Description = "Verifies that Target UIA element's Value or Name equals/contains Expected.")]
    [Keyword("UIA_CHECK_TEXT")]
    [Keyword("UIA_CHECK_VALUE")]
    public static bool VerifyValue(AutomationElement? el, string expected)
    {
        if (el == null) return false;
        string actual = GetValue(el);
        string name = el.Current.Name ?? "";
        bool match = actual.Equals(expected, StringComparison.OrdinalIgnoreCase)
                  || actual.Contains(expected, StringComparison.OrdinalIgnoreCase)
                  || name.Equals(expected, StringComparison.OrdinalIgnoreCase)
                  || name.Contains(expected, StringComparison.OrdinalIgnoreCase);

        SystemLogger.Log($"UIA_VERIFY | Expected: '{expected}' | Actual Value: '{actual}' | Name: '{name}' | Match: {match}",
            match ? SystemLogger.LogLevel.INFO : SystemLogger.LogLevel.ERROR);
        return match;
    }

    [Keyword("UIA_CHECK_TOGGLE", Description = "Checks that Target UIA ToggleState equals Expected (On, Off, Indeterminate).")]
    public static bool CheckToggleState(AutomationElement? el, string expected = "On")
    {
        if (el == null || !el.TryGetCurrentPattern(TogglePattern.Pattern, out var p)) return false;
        return ((TogglePattern)p).Current.ToggleState.ToString().Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    [Keyword("UIA_CHECK_SELECTED", Description = "Checks that Target UIA SelectionItem is selected.")]
    public static bool CheckSelected(AutomationElement? el, bool expected = true)
    {
        if (el == null || !el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p)) return false;
        return ((SelectionItemPattern)p).Current.IsSelected == expected;
    }

    [Keyword("UIA_CHECK_PROPERTY", Description = "Checks any extracted UIA attribute (PropertyName|ExpectedValue).")]
    public static bool CheckProperty(AutomationElement? el, string propertyName, string expectedValue)
    {
        if (el == null) return false;
        var attrs = ExtractAttributes(el);
        return attrs.TryGetValue(propertyName, out var actual) &&
               (actual.Equals(expectedValue, StringComparison.OrdinalIgnoreCase) ||
                actual.Contains(expectedValue, StringComparison.OrdinalIgnoreCase));
    }

    [Keyword("UIA_WAIT_FOR", Description = "Waits up to Value ms for Target UIA element to appear.")]
    public static bool WaitForElement(KeywordContext ctx, string target, int timeoutMs = 10000) =>
        FindElement(target, ctx.CurrentWindowHwnd, ctx.Controls, timeoutMs) != null;
}
