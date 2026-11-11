using System.Net;
using System.Text;
using WinFrame.Core.Execution;
using WinFrame.Core.Logging;

namespace WinFrame.Core.Reporting;

/// <summary>
/// Generates standalone HTML reports in both Dark and Light templates, including step details,
/// embedded failure/control screenshots, and separated System / User / Test log streams.
/// </summary>
public static class HtmlReportGenerator
{
    public static string GenerateBothThemes(QueueRunResult queue, string reportDir, string preferredTheme = "dark")
    {
        Directory.CreateDirectory(reportDir);
        string safeName = string.Concat(queue.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string baseFile = Path.Combine(reportDir, $"report_{safeName}_{queue.QueueId}");

        string darkPath = baseFile + "_dark.html";
        string lightPath = baseFile + "_light.html";

        File.WriteAllText(darkPath, BuildHtml(queue, isDark: true), Encoding.UTF8);
        File.WriteAllText(lightPath, BuildHtml(queue, isDark: false), Encoding.UTF8);

        SystemLogger.Log($"Generated HTML reports: {darkPath} and {lightPath}");
        return preferredTheme.Equals("light", StringComparison.OrdinalIgnoreCase) ? lightPath : darkPath;
    }

    public static string BuildHtml(QueueRunResult queue, bool isDark)
    {
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        string bg = isDark ? "#0d1117" : "#f8fafc";
        string cardBg = isDark ? "#161b22" : "#ffffff";
        string text = isDark ? "#e6edf3" : "#0f172a";
        string muted = isDark ? "#8b949e" : "#64748b";
        string border = isDark ? "#30363d" : "#e2e8f0";
        string accent = "#f59e0b";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine($"<html lang=\"en\" data-theme=\"{(isDark ? "dark" : "light")}\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine($"<title>WinFrame Report - {E(queue.Name)} ({(isDark ? "Dark" : "Light")})</title>");
        sb.AppendLine("<style>");
        sb.AppendLine($":root {{ --bg:{bg}; --card:{cardBg}; --text:{text}; --muted:{muted}; --border:{border}; --accent:{accent}; }}");
        sb.AppendLine("* { box-sizing:border-box; } body { margin:0; font-family:'Segoe UI',system-ui,sans-serif; background:var(--bg); color:var(--text); padding:24px; }");
        sb.AppendLine(".header { display:flex; justify-content:space-between; align-items:center; border-bottom:2px solid var(--accent); padding-bottom:16px; margin-bottom:24px; }");
        sb.AppendLine(".brand { font-size:13px; color:var(--accent); letter-spacing:0.08em; text-transform:uppercase; font-weight:700; }");
        sb.AppendLine(".cards { display:grid; grid-template-columns:repeat(auto-fit,minmax(170px,1fr)); gap:14px; margin-bottom:24px; }");
        sb.AppendLine(".card { background:var(--card); border:1px solid var(--border); border-radius:10px; padding:16px; }");
        sb.AppendLine(".card .label { font-size:12px; color:var(--muted); text-transform:uppercase; }");
        sb.AppendLine(".card .val { font-size:28px; font-weight:700; margin-top:4px; }");
        sb.AppendLine(".pass { color:#22c55e; } .fail { color:#ef4444; } .warn { color:#f59e0b; }");
        sb.AppendLine("table { width:100%; border-collapse:collapse; margin-top:10px; font-size:13px; }");
        sb.AppendLine("th, td { border-bottom:1px solid var(--border); padding:8px 10px; text-align:left; vertical-align:top; }");
        sb.AppendLine("th { color:var(--muted); font-weight:600; }");
        sb.AppendLine(".badge { display:inline-block; padding:2px 8px; border-radius:999px; font-size:11px; font-weight:700; }");
        sb.AppendLine(".badge-Passed { background:rgba(34,197,94,0.18); color:#22c55e; }");
        sb.AppendLine(".badge-Failed, .badge-Error { background:rgba(239,68,68,0.18); color:#ef4444; }");
        sb.AppendLine(".badge-Skipped, .badge-Stopped { background:rgba(245,158,11,0.18); color:#f59e0b; }");
        sb.AppendLine("pre.log { background:var(--card); border:1px solid var(--border); border-radius:8px; padding:12px; max-height:320px; overflow:auto; font-size:12px; font-family:Consolas,monospace; white-space:pre-wrap; }");
        sb.AppendLine("img.shot { max-width:260px; max-height:140px; border:1px solid var(--border); border-radius:6px; margin-top:4px; }");
        sb.AppendLine("button.theme-btn { background:var(--card); color:var(--text); border:1px solid var(--border); border-radius:6px; padding:8px 14px; cursor:pointer; font-weight:600; }");
        sb.AppendLine("</style>");
        sb.AppendLine("<script>");
        sb.AppendLine("function toggleTheme(){ const d=document.documentElement; const dark=d.getAttribute('data-theme')!=='dark'; d.setAttribute('data-theme',dark?'dark':'light');");
        sb.AppendLine("d.style.setProperty('--bg',dark?'#0d1117':'#f8fafc'); d.style.setProperty('--card',dark?'#161b22':'#ffffff'); d.style.setProperty('--text',dark?'#e6edf3':'#0f172a'); d.style.setProperty('--muted',dark?'#8b949e':'#64748b'); d.style.setProperty('--border',dark?'#30363d':'#e2e8f0'); }");
        sb.AppendLine("</script></head><body>");

        sb.AppendLine("<div class='header'><div>");
        sb.AppendLine("<div class='brand'>Hand of Glory Games and Software &bull; WinFrame Automation</div>");
        sb.AppendLine($"<h1 style='margin:4px 0 0 0'>{E(queue.Name)} &mdash; Execution Report</h1>");
        sb.AppendLine($"<div style='color:var(--muted);font-size:13px'>Machine: {E(queue.Machine)} &bull; Started: {queue.Started:yyyy-MM-dd HH:mm:ss} &bull; Duration: {queue.Duration.TotalSeconds:F2}s</div>");
        sb.AppendLine("</div><button class='theme-btn' onclick='toggleTheme()'>Switch Dark / Light</button></div>");

        if (queue.Stopped && !string.IsNullOrWhiteSpace(queue.StopReason))
        {
            sb.AppendLine($"<div class='card' style='border-color:#ef4444;margin-bottom:18px'><strong class='fail'>Queue Stopped:</strong> {E(queue.StopReason)}</div>");
        }

        sb.AppendLine("<div class='cards'>");
        sb.AppendLine($"<div class='card'><div class='label'>Total Tests</div><div class='val'>{queue.Tests.Count}</div></div>");
        sb.AppendLine($"<div class='card'><div class='label'>Passed</div><div class='val pass'>{queue.Passed}</div></div>");
        sb.AppendLine($"<div class='card'><div class='label'>Failed</div><div class='val fail'>{queue.Failed}</div></div>");
        sb.AppendLine($"<div class='card'><div class='label'>Skipped / Stopped</div><div class='val warn'>{queue.Skipped}</div></div>");
        sb.AppendLine($"<div class='card'><div class='label'>Pass Rate</div><div class='val'>{queue.PassRate * 100:F1}%</div></div>");
        sb.AppendLine("</div>");

        foreach (var t in queue.Tests)
        {
            sb.AppendLine("<div class='card' style='margin-bottom:18px'>");
            sb.AppendLine($"<div style='display:flex;justify-content:space-between;align-items:center'>");
            sb.AppendLine($"<h2 style='margin:0;font-size:18px'>{E(t.TestName)} <span class='badge badge-{t.Status}'>{t.Status}</span></h2>");
            sb.AppendLine($"<span style='color:var(--muted);font-size:12px'>RunId: {E(t.RunId)} &bull; Attempts: {t.Attempts} &bull; {t.Duration.TotalSeconds:F2}s</span></div>");
            if (!string.IsNullOrWhiteSpace(t.Error))
                sb.AppendLine($"<div class='fail' style='margin-top:6px;font-size:13px'>{E(t.Error)}</div>");

            sb.AppendLine("<table><thead><tr><th>#</th><th>Action</th><th>Target</th><th>Value</th><th>Status</th><th>Attempts</th><th>Time (ms)</th><th>Details / Snapshot</th></tr></thead><tbody>");
            foreach (var s in t.Steps)
            {
                string shotHtml = "";
                if (!string.IsNullOrWhiteSpace(s.Screenshot) && File.Exists(s.Screenshot))
                {
                    try
                    {
                        string b64 = Convert.ToBase64String(File.ReadAllBytes(s.Screenshot));
                        shotHtml = $"<br/><img class='shot' src='data:image/png;base64,{b64}' alt='snapshot'/>";
                    }
                    catch { }
                }
                sb.AppendLine($"<tr><td>{s.Index}</td><td><code>{E(s.Action)}</code></td><td><code>{E(s.Target)}</code></td><td><code>{E(s.Value)}</code></td>" +
                              $"<td><span class='badge badge-{s.Status}'>{s.Status}</span></td><td>{s.Attempts}</td><td>{s.DurationMs:F0}</td>" +
                              $"<td>{E(s.Message)}{shotHtml}</td></tr>");
            }
            sb.AppendLine("</tbody></table></div>");
        }

        // Separated Logs: System, User, Test
        AppendLogSection(sb, "Test Execution Logs", LogChannel.Test);
        AppendLogSection(sb, "System Run Logs", LogChannel.System);
        AppendLogSection(sb, "User Activity Logs", LogChannel.User);

        sb.AppendLine("<footer style='margin-top:32px;color:var(--muted);font-size:12px;text-align:center'>");
        sb.AppendLine("Generated by WinFrame Automation &bull; Hand of Glory Games and Software &bull; Author: Zak Millikin (Zakskywalker M_A.P.) &bull; AI: Google DeepMind Antigravity (Claude Opus &amp; Gemini) &bull; Splash Art: <a href='https://perchance.org/cursed-ai' style='color:var(--accent)'>Perchance AI (Cursed)</a> &bull; Licensed under <a href='https://www.gnu.org/licenses/gpl-3.0.html' style='color:var(--accent)'>GNU GPL v3.0</a></footer>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendLogSection(StringBuilder sb, string title, LogChannel channel)
    {
        var entries = SystemLogger.Recent(channel, max: 300);
        sb.AppendLine($"<h3 style='margin-top:24px'>{WebUtility.HtmlEncode(title)} ({entries.Count})</h3>");
        sb.AppendLine("<pre class='log'>");
        foreach (var e in entries) sb.AppendLine(WebUtility.HtmlEncode(e.ToString()));
        sb.AppendLine("</pre>");
    }
}
