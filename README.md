# WinFrame Automation Framework 1.0

<p align="center">
  <img src="assets/splash.jpg" alt="Hand of Glory Games and Software Splash Logo" width="240" />
</p>

**Hand of Glory Games and Software** • **Author:** Zak Millikin (Zakskywalker M_A.P.) • **License:** [GNU GPL v3.0](https://www.gnu.org/licenses/gpl-3.0.html)

WinFrame is a local and remote keyword-driven + object-oriented Windows test automation framework built on **.NET 10 (`net10.0-windows`)**. It combines:

- **Win32 Native Automation (`Win32Automation`, `ScreenLocationManager`, `NativeMethods`)**: Multi-monitor coordinate interpolation, window tree harvesting, native `SendInput` mouse & Unicode keyboard input, and `WM_GETTEXT` / `WM_SETTEXT` verification.
- **Win32 GDI Control Outline Highlighter (`Win32Highlighter`)**: Draws configurable colored outlines (`#F59E0B`, `Red`, `Lime`, etc.) directly onto the screen DC for a configurable short interval whenever the `highlight` variable is set to `1` or `true` (and turns off when set to `0`, `""`, or `null`). Includes the `HIGHLIGHT` keyword command to outline controls by name from any keyword sheet.
- **Microsoft UI Automation (`UiaAutomation`)**: Full UIA pattern support (`Invoke`, `Value`, `Toggle`, `SelectionItem`, `ExpandCollapse`, `RangeValue`, `Scroll`, `Text`, `Window`), deep attribute scraping, and checks callable in keyword sheets just like native methods.
- **OpenCvSharp4 Vision & Snipping (`OpenCvAutomation`)**: Full virtual screen, monitor, application window, individual control, and interactive Snipping-Tool region captures; template matching (`CV_CLICK_IMAGE`, `CV_WAIT_IMAGE`, `CV_VERIFY_IMAGE`, `CV_COMPARE`); and automatic or **second-pass replay** recording of OpenCV snapshots into the Control Sheet.
- **Developer Step Queue, Rule-Based Inversion & Range Execution (`StepQueueController`, `StepInversionEngine`)**: Step forward (`StepNext`), step backward with automatic rule-based inversion (`StepPrevious`: entering text becomes deleting text, checking becomes unchecking, expanding becomes collapsing, starting app becomes stopping app), or execute any forward or reverse step range (`"1-3"`, `"3-4"`, `"4-1"`).
- **Control Sheet / Object Repository (`ControlRepository`)**: Persists `controlName => attributes + image + windowImage` in **JSON**, **CSV**, **Excel (`.xlsx`)**, or **MySQL**, with selectable image encoding: local file (`file=`), raw Base64 (`b=`), Deflate compressed (`c=`), or LZMA 7-Zip compressed (`7zc=`).
- **Process Management Engine (`ProcessManagementEngine`)**: Starts, attaches to, monitors, and automatically re-harnesses lost or restarted applications under test.
- **Separated Logging & HTML Reporting (`SystemLogger`, `HtmlReportGenerator`)**: Separates **System Run Logs**, **User Logs**, and **Test Logs**, and generates standalone **Dark** and **Light** HTML reports.
- **ClickOnce Auto-Updater & Windows 11 Setup Wizard (`AutoUpdateManager`, `WinFrame.Installer`)**: Checks GitHub Pages (`version.json` / ClickOnce `setup.exe` / `WinFrame.Installer.exe`) for updates automatically or on-demand from **About -> Check for Updates Now** and `WinFrame.Cli update`.

---

## 3-Stage Live Recording Flow & Example Test Suites (`samples/`)

### 1. Live Record Flow (`● Record Test`)
Clicking **`● Record Test`** in `WinFrame.App` launches a 3-stage workflow:
1. **Stage 1 — Docked Live Recording Bar (`RecordingBarForm`)**:
   - Automatically hides `MainForm` (`this.Hide()`) so the Studio window is removed from initial recording.
   - Docks a compact bar at the **bottom** or **right side** of the screen (`Dock: Bottom ⇄ Side`).
   - Uses **both** **Mouse-Position UIA Scrape** (`UiaAutomation.ElementFromPoint`) and the **Window-to-Control Position Function** (`ScreenLocationManager.GetWindowToControlPosition`) to identify the control under the mouse (`@ctrltype_x`), its relative coordinates inside the window (`Win32.WindowRelX/Y`), and its relative coordinates inside the control (`Win32.ControlRelX/Y`), alongside **keyboard data input** (`TYPE`, `SEND_KEYS`).
   - Displays each recorded step in a live editable table right inside the bar so you can inspect or tweak steps on the fly.
2. **Stage 2 — Test Case Layout & Step Editor (`RecordedTestEditorDialog`)**:
   - When you click **`■ Stop Recording & Edit Steps`**, presents the full Test Case layout with **Action**, **Control (`@name`)**, **Step Function / Value**, and **Window-to-Control Position**, plus a right-hand **Scraped Control Inspector** with OpenCV preview and control renaming.
   - Includes **`+ Add Step`**, **`▲ Move Up`**, **`▼ Move Down`**, **`✕ Delete Step`**, and **`★ Outline Control (Win32)`** so you can refine steps one by one.
3. **Stage 3 — Custom Save & TestBed Repository Dialog (`SaveRecordedTestDialog`)**:
   - When you click **`Done — Choose Save Location & TestBed ->`**, lets you pick the save folder & format (`.csv`, `.json`, `.xlsx`, or all three) and either select an **existing TestBed** from the config collection or **choose a new TestBed executable/folder and give it a name** to store permanently in `ConfigManager` (`testbeds.json`).

### 2. Example Tests & Test Cases (`samples/`)
Ready-to-run sample tests and control repositories for every supported format are included in `samples/`:
- **CSV (`samples/csv/`)**:
  - `samples/csv/01_calculator_win32_and_uia.csv` — Calculator addition with `HIGHLIGHT`, `UIA_INVOKE`, and variable assertions.
  - `samples/csv/02_notepad_typing_and_inverse_rollback.csv` — Notepad keyboard input (`TYPE`, `SEND_KEYS`) and step inversion demo.
  - `samples/csv/03_window_to_control_and_opencv.csv` — Window-to-control relative coordinate clicks (`CLICK_RELATIVE`) and OpenCV template verification (`CV_VERIFY_IMAGE`).
- **JSON (`samples/json/`)**:
  - `samples/json/01_calculator_suite.json` — Multi-test JSON suite (`Calculator_Addition_And_Highlight_JSON` & `Checkbox_Toggle_And_Inverse_JSON`).
  - `samples/json/02_notepad_keyboard_and_rollback.json` — Notepad keyboard entry and rollback JSON test case.
- **Excel `.xlsx` (`samples/xlsx/`)**:
  - `samples/xlsx/01_winframe_multi_sheet_suite.xlsx` — Multi-worksheet Excel workbook containing multiple test cases.
- **C# Object-Oriented (`samples/oop/`)**:
  - `samples/oop/SampleOopTestCases.cs` — Strongly-typed `ScreenObject` (`SampleLoginScreen`) and `TestBase` (`SampleCalculatorAndRollbackOopTest`) examples.
- **Control Repository Sheets (`samples/controls/`)**:
  - `samples/controls/sample_controls.json`, `samples/controls/sample_controls.csv`, and `samples/controls/sample_controls.xlsx`.

---

## Antivirus / Windows Defender False-Positive Mitigation

Why UI automation tools can trigger heuristic AV alerts — and what is built into WinFrame to prevent it:
1. **No Global `WH_KEYBOARD_LL` / `WH_MOUSE_LL` Keylogger Hooks**: Global low-level hooks (`SetWindowsHookEx` 13 & 14) are the #1 heuristic trigger for `Behavior:Win32/Keylogger` / `Wacatac` in Windows Defender. `TestRecorder` has been engineered to use **hook-free UIA + Window-to-Control Position & edge-triggered state sampling** active *only* while the Recording Bar is open.
2. **Embedded Windows 10/11 `asInvoker` Application Manifest (`src/WinFrame.App/app.manifest`) & Deterministic Builds**: Declares explicit `<requestedExecutionLevel level="asInvoker" uiAccess="false" />`, Windows 10/11 compatibility GUIDs, and deterministic PE headers.
3. **Local Authenticode Code-Signing & Defender Exclusion Helper (`installer/trust_and_defender_setup.ps1`)**:
   If Windows Defender or SmartScreen flags a locally compiled unsigned `.exe`, run:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\installer\trust_and_defender_setup.ps1
   ```
   This script creates a local `Hand of Glory Games and Software` code-signing certificate, signs all `WinFrame*.exe` / `WinFrame*.dll` binaries with SHA-256 Authenticode, and (when run as Administrator) adds the repo and `%LOCALAPPDATA%\Programs\WinFrame` to Windows Defender exclusions.

---

## Projects in `WinFrame.sln`

| Project | Output | Purpose |
|---|---|---|
| `src/WinFrame.Core` | `WinFrame.Core.dll` | Reusable class library that can be referenced by any WinForms, WPF, or console project |
| `src/WinFrame.App` | `WinFrame.App.exe` | Desktop Studio with Hand of Glory splash screen, tray icon + scrolling marquee, Load Tests Queue, Step/Inverse Range Controller, Create Tests Recorder + Accessory Control Naming Panel, Settings, Theme Picker, and About / Automation IDs JSON export + Auto-Updater |
| `src/WinFrame.Cli` | `WinFrame.Cli.exe` | Console CLI for running queues, step ranges, inverse rollbacks, scraping controls, exporting Automation IDs, checking updates, and hosting the remote API |
| `src/WinFrame.Service` | `WinFrame.Service.exe` | Dedicated remote HTTP/REST service host for remote test & step-queue execution |
| `src/WinFrame.Installer` | `WinFrame.Installer.exe` | Standalone Windows 11 GUI Setup Wizard (Shortcuts, Startup registration, User PATH, and Uninstaller) |
| `tests/WinFrame.Tests` | xUnit Test Suite | Unit and integration tests for OOP extensibility, step inversion, range execution, image codecs, control stores, Win32 highlighting, HTML reporting, and auto-update version comparison |

---

## Local Debugging in IDE (`F5`) & Terminal Commands

### 1. Debugging in Antigravity / VS Code (`F5`)
`.vscode/launch.json` and `.vscode/tasks.json` are pre-configured with 5 launch targets (`Ctrl+Shift+D` -> press **`F5`**):
1. **Debug WinFrame Studio (WinForms App)** — Launches the full GUI Studio (`WinFrame.App.exe`).
2. **Debug WinFrame CLI (Run Sample Sheet)** — Runs `samples/highlight_and_inverse_demo.csv` in the integrated terminal.
3. **Debug WinFrame CLI (Reverse Step Range 6-4)** — Runs steps 6 down to 4 in reverse with automatic rule-based inversion (`StepInversionEngine`).
4. **Debug WinFrame Remote API Service** — Starts the remote HTTP REST API server on `http://localhost:5880/`.
5. **Debug WinFrame Setup Wizard (Installer)** — Launches the Windows 11 GUI Setup Wizard (`WinFrame.Installer.exe`).

### 2. Terminal Build, Test & Run Commands (PowerShell)

```powershell
# 1. Build the entire solution (Debug or Release)
dotnet build WinFrame.sln
dotnet build WinFrame.sln -c Release

# 2. Run all xUnit unit & integration tests
dotnet test WinFrame.sln

# 3. Run the WinForms Desktop Studio GUI
dotnet run --project src/WinFrame.App/WinFrame.App.csproj

# 4. Run a Keyword Sheet from the CLI
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- run samples/highlight_and_inverse_demo.csv

# 5. Run a Step Range Forward (1-3) or in Reverse with Automatic Rule Inversion (6-4)
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- range samples/highlight_and_inverse_demo.csv 1-3
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- range samples/highlight_and_inverse_demo.csv 6-4

# 6. List all registered Win32, UIA, OpenCV, Highlight, Process, and Flow Keywords
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- keywords

# 7. Check for Updates from GitHub Pages via CLI
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- update --check

# 8. Host the Remote REST API & Step-Queue Server on port 5880
dotnet run --project src/WinFrame.Service/WinFrame.Service.csproj -- http://localhost:5880/

# 9. Launch the Windows 11 GUI Setup Wizard or Build the Installer Package
dotnet run --project src/WinFrame.Installer/WinFrame.Installer.csproj
powershell -ExecutionPolicy Bypass -File .\installer\build_installer.ps1
```

---

## Installer, Auto-Updater & GitHub Actions Pages Flow

- **ClickOnce Publish Profile (`src/WinFrame.App/Properties/PublishProfiles/GitHubPages.pubxml`)**: Configures `WinFrame.App` for ClickOnce auto-updates served from `https://zakskywalker.github.io/winframe-automation/`.
- **AutoUpdateManager (`src/WinFrame.Core/Update/AutoUpdateManager.cs`)**: Queries `version.json` and ClickOnce `WinFrame.App.application` on GitHub Pages, compares semantic/build versions, and downloads & launches `setup.exe` or `WinFrame.Installer.exe` directly from **About -> Check for Updates Now** or `WinFrame.Cli update`.
- **Windows 11 Setup Wizard (`src/WinFrame.Installer` & `installer/`)**:
  - `installer/build_installer.ps1`: Publishes `WinFrame.App`, `WinFrame.Cli`, `WinFrame.Service`, `WinFrame.Core.dll`, and `WinFrame.Installer.exe` into `dist/WinFrame-win-x64` (and compiles an Inno Setup installer if `ISCC.exe` is installed).
  - `installer/install_windows11.ps1`: Silent or interactive PowerShell installer for Windows 11 with Start Menu, Desktop shortcut, Startup registration, User `PATH`, and `Uninstall_WinFrame.ps1`.
- **GitHub Actions Workflows (`.github/workflows/pages.yml` & `.github/workflows/ci-release.yml`)**:
  - Restores, builds, and runs all xUnit tests in `WinFrame.sln` on `windows-latest`.
  - Publishes the ClickOnce package (`setup.exe` + `WinFrame.App.application` with auto-incremented `1.0.0.<run_number>`), `WinFrame.Installer.exe`, `WinFrame.Core.dll`, `version.json`, and the Hand of Glory stylized landing page (`.github/pages/index.html` + `assets/splash.jpg`), uploads the `clickonce-site` and `winframe-release-bundle` artifacts, and deploys to GitHub Pages.

---

## Versioning & Pre-Submit Checklist (Before Pushing to GitHub)

### 1. Versioning Architecture
- **Current Release Line:** `1.0.0` (`Major.Minor.Patch`), configured centrally in `Directory.Build.props` (`<Version>1.0.0</Version>`).
- **Automated ClickOnce & GitHub Pages Build Version:** `1.0.0.<GITHUB_RUN_NUMBER>` (`Major.Minor.Patch.Revision`).
  - On every push to `main`, `.github/workflows/pages.yml` passes `/property:ApplicationVersion=1.0.0.$env:GITHUB_RUN_NUMBER` to MSBuild and writes the matching version into `clickonce/version.json`.
  - Both Windows ClickOnce (`setup.exe` / `WinFrame.App.application`) and `AutoUpdateManager.CheckForUpdateAsync()` compare the 4-part version so every push to `main` is automatically recognized as an update.
- **Files to Update When Bumping `Major.Minor.Patch` (e.g., `1.0.0` -> `1.1.0`):**
  1. `Directory.Build.props` — `<Version>1.1.0</Version>`
  2. `.github/workflows/pages.yml` — `$version = "1.1.0.$env:GITHUB_RUN_NUMBER"`
  3. `docs/version.json` — `"version": "1.1.0.0"`
  4. `installer/setup.iss`, `installer/build_installer.ps1`, and `src/WinFrame.Installer/InstallerWizardForm.cs` (`DisplayVersion`)
  5. `.github/pages/index.html` and `docs/index.html` badge text

### 2. Pre-Push / Pre-Submit Checklist
Before committing and pushing changes to GitHub (`origin/main`), run through these steps:

```powershell
# Step 1: Build the full solution in Release mode and ensure 0 errors
dotnet build WinFrame.sln -c Release --nologo

# Step 2: Run the entire xUnit test suite and verify all tests pass
dotnet test WinFrame.sln -c Release --nologo

# Step 3: Smoke-test CLI keywords & update check
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- keywords
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- run samples/highlight_and_inverse_demo.csv

# Step 4: (Optional) Rebuild local Windows 11 distribution & Setup Wizard bundle
powershell -ExecutionPolicy Bypass -File .\installer\build_installer.ps1

# Step 5: Inspect staged files to ensure no local logs, reports, or secrets are included
git status
```

- **Do Not Commit Local Runtime State:** Ensure runtime directories (`logs/`, `reports/`, `controls_images/`, `bin/`, `obj/`, `publish/`, `dist/`) and `%APPDATA%\WinFrame\config.json` (which holds DPAPI-encrypted credentials and MySQL connection strings) stay untracked via `.gitignore`.
- **Verify CI/CD After Pushing:**
  ```powershell
  git add -A
  git commit -m "Describe your change"
  git push origin main
  & "$env:LOCALAPPDATA\Programs\gh\bin\gh.exe" run list --limit 5
  ```

---

## Versioning & Pre-Submit Checklist (Before Pushing to GitHub)

### 1. Versioning Architecture
- **Current Release Line:** `1.0.0` (`Major.Minor.Patch`), configured centrally in `Directory.Build.props` (`<Version>1.0.0</Version>`).
- **Automated ClickOnce & GitHub Pages Build Version:** `1.0.0.<GITHUB_RUN_NUMBER>` (`Major.Minor.Patch.Revision`).
  - On every push to `main`, `.github/workflows/pages.yml` passes `/property:ApplicationVersion=1.0.0.$env:GITHUB_RUN_NUMBER` to MSBuild and writes the matching version into `clickonce/version.json`.
  - Both Windows ClickOnce (`setup.exe` / `WinFrame.App.application`) and `AutoUpdateManager.CheckForUpdateAsync()` compare the 4-part version so every push to `main` is automatically recognized as an update.
- **Files to Update When Bumping `Major.Minor.Patch` (e.g., `1.0.0` -> `1.1.0`):**
  1. `Directory.Build.props` — `<Version>1.1.0</Version>`
  2. `.github/workflows/pages.yml` — `$version = "1.1.0.$env:GITHUB_RUN_NUMBER"`
  3. `docs/version.json` — `"version": "1.1.0.0"`
  4. `installer/setup.iss`, `installer/build_installer.ps1`, and `src/WinFrame.Installer/InstallerWizardForm.cs` (`DisplayVersion`)
  5. `.github/pages/index.html` and `docs/index.html` badge text

### 2. Pre-Push / Pre-Submit Checklist
Before committing and pushing changes to GitHub (`origin/main`), run through these steps:

```powershell
# Step 1: Build the full solution in Release mode and ensure 0 errors
dotnet build WinFrame.sln -c Release --nologo

# Step 2: Run the entire xUnit test suite and verify all tests pass
dotnet test WinFrame.sln -c Release --nologo

# Step 3: Smoke-test CLI keywords & update check
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- keywords
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- run samples/highlight_and_inverse_demo.csv

# Step 4: (Optional) Rebuild local Windows 11 distribution & Setup Wizard bundle
powershell -ExecutionPolicy Bypass -File .\installer\build_installer.ps1

# Step 5: Inspect staged files to ensure no local logs, reports, or secrets are included
git status
```

- **Do Not Commit Local Runtime State:** Ensure runtime directories (`logs/`, `reports/`, `controls_images/`, `bin/`, `obj/`, `publish/`, `dist/`) and `%APPDATA%\WinFrame\config.json` (which holds DPAPI-encrypted credentials and MySQL connection strings) stay untracked via `.gitignore`.
- **Verify CI/CD After Pushing:**
  ```powershell
  git add -A
  git commit -m "Describe your change"
  git push origin main
  & "$env:LOCALAPPDATA\Programs\gh\bin\gh.exe" run list --limit 5
  ```

---

---

## Local Debugging in IDE (`F5`) & Terminal Commands

### 1. Debugging in Antigravity / VS Code (`F5`)
`.vscode/launch.json` and `.vscode/tasks.json` are pre-configured with 5 launch targets (`Ctrl+Shift+D` -> press **`F5`**):
1. **Debug WinFrame Studio (WinForms App)** — Launches the full GUI Studio (`WinFrame.App.exe`).
2. **Debug WinFrame CLI (Run Sample Sheet)** — Runs `samples/highlight_and_inverse_demo.csv` in the integrated terminal.
3. **Debug WinFrame CLI (Reverse Step Range 6-4)** — Runs steps 6 down to 4 in reverse with automatic rule-based inversion (`StepInversionEngine`).
4. **Debug WinFrame Remote API Service** — Starts the remote HTTP REST API server on `http://localhost:5880/`.
5. **Debug WinFrame Setup Wizard (Installer)** — Launches the Windows 11 GUI Setup Wizard (`WinFrame.Installer.exe`).

### 2. Terminal Build, Test & Run Commands (PowerShell)

```powershell
# 1. Build the entire solution (Debug or Release)
dotnet build WinFrame.sln
dotnet build WinFrame.sln -c Release

# 2. Run all xUnit unit & integration tests
dotnet test WinFrame.sln

# 3. Run the WinForms Desktop Studio GUI
dotnet run --project src/WinFrame.App/WinFrame.App.csproj

# 4. Run a Keyword Sheet from the CLI
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- run samples/highlight_and_inverse_demo.csv

# 5. Run a Step Range Forward (1-3) or in Reverse with Automatic Rule Inversion (6-4)
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- range samples/highlight_and_inverse_demo.csv 1-3
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- range samples/highlight_and_inverse_demo.csv 6-4

# 6. List all registered Win32, UIA, OpenCV, Highlight, Process, and Flow Keywords
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- keywords

# 7. Check for Updates from GitHub Pages via CLI
dotnet run --project src/WinFrame.Cli/WinFrame.Cli.csproj -- update --check

# 8. Host the Remote REST API & Step-Queue Server on port 5880
dotnet run --project src/WinFrame.Service/WinFrame.Service.csproj -- http://localhost:5880/

# 9. Launch the Windows 11 GUI Setup Wizard or Build the Installer Package
dotnet run --project src/WinFrame.Installer/WinFrame.Installer.csproj
powershell -ExecutionPolicy Bypass -File .\installer\build_installer.ps1
```

---

## Installer, Auto-Updater & GitHub Actions Pages Flow

- **ClickOnce Publish Profile (`src/WinFrame.App/Properties/PublishProfiles/GitHubPages.pubxml`)**: Configures `WinFrame.App` for ClickOnce auto-updates served from `https://zakskywalker.github.io/winframe-automation/`.
- **AutoUpdateManager (`src/WinFrame.Core/Update/AutoUpdateManager.cs`)**: Queries `version.json` and ClickOnce `WinFrame.App.application` on GitHub Pages, compares semantic/build versions, and downloads & launches `setup.exe` or `WinFrame.Installer.exe` directly from **About -> Check for Updates Now** or `WinFrame.Cli update`.
- **Windows 11 Setup Wizard (`src/WinFrame.Installer` & `installer/`)**:
  - `installer/build_installer.ps1`: Publishes `WinFrame.App`, `WinFrame.Cli`, `WinFrame.Service`, `WinFrame.Core.dll`, and `WinFrame.Installer.exe` into `dist/WinFrame-win-x64` (and compiles an Inno Setup installer if `ISCC.exe` is installed).
  - `installer/install_windows11.ps1`: Silent or interactive PowerShell installer for Windows 11 with Start Menu, Desktop shortcut, Startup registration, User `PATH`, and `Uninstall_WinFrame.ps1`.
- **GitHub Actions Workflows (`.github/workflows/pages.yml` & `.github/workflows/ci-release.yml`)**:
  - Restores, builds, and runs all xUnit tests in `WinFrame.sln` on `windows-latest`.
  - Publishes the ClickOnce package (`setup.exe` + `WinFrame.App.application` with auto-incremented `1.0.0.<run_number>`), `WinFrame.Installer.exe`, `WinFrame.Core.dll`, `version.json`, and the Hand of Glory stylized landing page (`.github/pages/index.html` + `assets/splash.jpg`), uploads the `clickonce-site` and `winframe-release-bundle` artifacts, and deploys to GitHub Pages.

---

## Credits & Attribution

- **Creator & Lead Architect:** Zak Millikin (**Zakskywalker M_A.P.**) — *Hand of Glory Games and Software*
- **AI Pair Programmers:** Google DeepMind **Antigravity** (*Claude Opus* & *Gemini*)
- **Splash Screen Artwork:** [**Perchance AI (Cursed)**](https://perchance.org/cursed-ai)
- **License:** [GNU General Public License v3.0 (GPL-3.0-or-later)](https://www.gnu.org/licenses/gpl-3.0.html)
