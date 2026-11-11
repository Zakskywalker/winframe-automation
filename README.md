# WinFrame Automation Framework

**Hand of Glory Games and Software** • **Author:** Zak Millikin • **License:** [GNU GPL v3.0](https://www.gnu.org/licenses/gpl-3.0.html)

WinFrame is a local and remote keyword-driven + object-oriented Windows test automation framework built on **.NET 10 (`net10.0-windows`)**. It combines:

- **Win32 Native Automation (`Win32Automation`, `ScreenLocationManager`, `NativeMethods`)**: Multi-monitor coordinate interpolation, window tree harvesting, native `SendInput` mouse & Unicode keyboard input, and `WM_GETTEXT` / `WM_SETTEXT` verification.
- **Win32 GDI Control Outline Highlighter (`Win32Highlighter`)**: Draws configurable colored outlines (`#F59E0B`, `Red`, `Lime`, etc.) directly onto the screen DC for a configurable short interval whenever the `highlight` variable is set to `1` or `true` (and turns off when set to `0`, `""`, or `null`). Includes the `HIGHLIGHT` keyword command to outline controls by name from any keyword sheet.
- **Microsoft UI Automation (`UiaAutomation`)**: Full UIA pattern support (`Invoke`, `Value`, `Toggle`, `SelectionItem`, `ExpandCollapse`, `RangeValue`, `Scroll`, `Text`, `Window`), deep attribute scraping, and checks callable in keyword sheets just like native methods.
- **OpenCvSharp4 Vision & Snipping (`OpenCvAutomation`)**: Full virtual screen, monitor, application window, individual control, and interactive Snipping-Tool region captures; template matching (`CV_CLICK_IMAGE`, `CV_WAIT_IMAGE`, `CV_VERIFY_IMAGE`, `CV_COMPARE`); and automatic or **second-pass replay** recording of OpenCV snapshots into the Control Sheet.
- **Developer Step Queue, Rule-Based Inversion & Range Execution (`StepQueueController`, `StepInversionEngine`)**: Step forward (`StepNext`), step backward with automatic rule-based inversion (`StepPrevious`: entering text becomes deleting text, checking becomes unchecking, expanding becomes collapsing, starting app becomes stopping app), or execute any forward or reverse step range (`"1-3"`, `"3-4"`, `"4-1"`).
- **Control Sheet / Object Repository (`ControlRepository`)**: Persists `controlName => attributes + image + windowImage` in **JSON**, **CSV**, **Excel (`.xlsx`)**, or **MySQL**, with selectable image encoding: local file (`file=`), raw Base64 (`b=`), Deflate compressed (`c=`), or LZMA 7-Zip compressed (`7zc=`).
- **Process Management Engine (`ProcessManagementEngine`)**: Starts, attaches to, monitors, and automatically re-harnesses lost or restarted applications under test.
- **Separated Logging & HTML Reporting (`SystemLogger`, `HtmlReportGenerator`)**: Separates **System Run Logs**, **User Logs**, and **Test Logs**, and generates standalone **Dark** and **Light** HTML reports.

## Projects in `WinFrame.sln`

| Project | Output | Purpose |
|---|---|---|
| `src/WinFrame.Core` | `WinFrame.Core.dll` | Reusable class library that can be referenced by any WinForms, WPF, or console project |
| `src/WinFrame.App` | `WinFrame.App.exe` | Desktop Studio with Hand of Glory splash screen, tray icon + scrolling marquee, Load Tests Queue, Step/Inverse Range Controller, Create Tests Recorder + Accessory Control Naming Panel, Settings, Theme Picker, and About / Automation IDs JSON export |
| `src/WinFrame.Cli` | `WinFrame.Cli.exe` | Console CLI for running queues, step ranges, inverse rollbacks, scraping controls, exporting Automation IDs, and hosting the remote API |
| `src/WinFrame.Service` | `WinFrame.Service.exe` | Dedicated remote HTTP/REST service host for remote test & step-queue execution |
| `tests/WinFrame.Tests` | xUnit Test Suite | Unit and integration tests for OOP extensibility, step inversion, range execution, image codecs, control stores, Win32 highlighting, and HTML reporting |

## Credits & Attribution

- **Creator & Lead Architect:** Zak Millikin (**Zakskywalker M_A.P.**) — *Hand of Glory Games and Software*
- **AI Pair Programmers:** Google DeepMind **Antigravity** (*Claude Opus* & *Gemini*)
- **Splash Screen Artwork:** [**Perchance AI (Cursed)**](https://perchance.org/cursed-ai)
- **License:** [GNU General Public License v3.0 (GPL-3.0-or-later)](https://www.gnu.org/licenses/gpl-3.0.html)

