# Taskbar Monitor

[한국어](README.md) | [English](README.en.md)

The application interface is currently in Korean. This English README translates the documentation; an in-app language selector is not available yet. Korean labels are included below to help you find controls.

> **Beta software.** This application is still under development. Unexpected bugs, display issues, and compatibility problems may occur. Report your environment and reproduction steps through [GitHub Issues](https://github.com/svsivsv/TaskbarMonitor/issues). Remove personal information from screenshots or logs before attaching them.

A native Windows 11 x64 taskbar widget showing CPU, memory, disk, network, and GPU usage, reference temperature readings, and miniature graphs. Development and testing currently target Intel/AMD 64-bit PCs with the standard horizontal Windows 11 taskbar.

For your first run, keep the defaults and click **Save and turn on widget (`저장하고 위젯 켜기`)**. If the widget is missing, check the status message at the bottom of Settings. When taskbar space is insufficient, switch to the above-taskbar or popup mode. **Save and apply (`저장·적용`)** also saves settings, but does not force a hidden widget to appear.

**[Download TaskbarMonitor.exe](https://github.com/svsivsv/TaskbarMonitor/raw/refs/heads/main/release/TaskbarMonitor.exe)** · [File in this repository](release/TaskbarMonitor.exe)

The download points to `release/TaskbarMonitor.exe` in this repository. The local project and GitHub use the same source, documentation, and EXE layout. A single [release page](https://github.com/svsivsv/TaskbarMonitor/releases/tag/current) provides the same EXE. Release names and tags do not contain version numbers. To identify a particular binary, use its SHA-256 fingerprint:

```powershell
Get-FileHash .\TaskbarMonitor.exe -Algorithm SHA256
```

The default file version displayed by Windows is not used as the release identifier.

## Screenshots

### Compact widget

![Compact widget with CPU and GPU temperature readings, usage values, and miniature graphs](docs/widget-compact.png)

### Inside the Windows 11 taskbar

![Taskbar Monitor between the Windows weather button and the Start and Search buttons](docs/widget-taskbar.png)

The widget reads the actual positions of the Windows weather and Start buttons and fits into the available space between them. Position queries run in a background task. If the space is narrower than 48 px or the Start button position cannot be determined, the widget hides to avoid covering other buttons and explains the situation in the tray tooltip. Use the tray context menu to switch to above-taskbar or popup mode. Inside-taskbar display resumes when space becomes available. **Transparent taskbar background (`작업표시줄 배경 투명`)** shows only text and graphs instead of a separate panel.

Because Windows 11's XAML taskbar covers ordinary Win32 child windows, inside-taskbar mode uses a taskbar-owned overlay aligned with the free space as a compatibility approach. Its bounds are constrained to that space. It temporarily hides when Start or Search opens so it does not cover Windows controls. This is separate from the independent popup mode.

## Features

At startup, the widget waits for the taskbar layout to remain consistent for 0.5 seconds before appearing. It rechecks positions more frequently during the first five seconds and immediately after opening or closing Settings, then approximately every two seconds. Reduced space is applied when detected; expanded space is applied after it stabilizes. Windows response delays can postpone positioning updates.

- Show or hide CPU, memory, disk, network, and GPU separately.
- Display CPU and GPU temperatures between the item name and usage value, with independent temperature toggles.
- Keep item columns equally wide in inside-taskbar mode. With **Auto-fit to space**, disabling items does not shrink the overall allocated width: remaining items share it equally. Temperature text is not horizontally compressed. It uses `58°C` or `58°` according to available space, and is omitted if neither fits.
- Choose memory values as percent, used GB, or used/total GB.
- Select multiple drives, such as C: and E:, each with its own disk activity value and graph.
- Show `—` when a selected drive cannot be read instead of substituting another drive's value. Missing samples leave gaps in graphs.
- Calculate text height using the actual font and display scaling. If text exceeds the widget height, the font scales down proportionally rather than being squeezed horizontally. Graphs are omitted when no vertical space remains; increase inside height or popup height for larger text.
- Toggle numbers and graphs independently for each item.
- Customize names, order, colors, and graph styles per item.
- Clip long names within their own label area without covering temperatures, usage values, or adjacent items. A fixed gap separates temperature and usage.
- Choose line, filled, or bar graphs.
- Set a refresh interval of 0.2–10 seconds and a graph history of 10–600 seconds.
- Use real sample timestamps for the graph's horizontal axis. Samples older than the selected history window expire even after a pause or slower hidden sampling. Long gaps are not joined by a continuous line.
- Choose inside-taskbar, above-taskbar, or tray-toggle popup mode.
- Optionally enable left/right overflow pages when space is limited.
- Fill the final page with some items from the previous page to reduce single-item pages. If only one item fits, pages still show one item at a time.
- Set popup width and height numerically or drag its lower-right corner. The default size is 500 × 72 px.
- Use normal window ordering by default; explicitly select always-on-top or always-behind when wanted.
- Choose whether the popup appears at startup; the default is enabled. A manually hidden popup does not reappear simply because Start or Search opened and closed.
- Enable interaction across the entire widget or make it completely click-through.
- Hide in fullscreen, always show, or show while passing clicks through.
- Stop measurement while hidden by default, or select five-second sampling or continued normal sampling.
- Start automatically at Windows sign-in with saved settings.
- Avoid treating capture tools, the desktop, and ordinary maximized windows as fullscreen applications.
- Give Windows Start and Search priority over the widget.
- Continue updating numbers and graphs while Settings or menus are in use without reapplying window ordering or display mode on every sample. Start, Search, and the selected fullscreen hiding policy retain priority even with Settings open.
- View graphs in the widget and Settings preview only; no separate detailed-graph window opens.

## Mouse controls

| Action | Result |
| --- | --- |
| Single click | Does not open another window. In popup mode, hold and drag to move the widget. |
| Double-click | Open Windows Task Manager. |
| Right-click | Open settings, display-mode, show/hide, interaction, window-order, pause, Task Manager, and exit controls. |
| Click outside the context menu or right-click again | Close the menu. |
| Left-click the tray icon | Toggle the widget's visibility, including in popup mode. |
| Right-click the tray icon | Open the same management menu. |
| Drag the popup body | Move the popup while it is not pinned. |
| Drag the popup's lower-right corner | Resize the popup and save its size when released. |
| Pin current popup position (`현재 팝업 위치 고정`) in Settings | Save screen coordinates and lock movement and resizing; disable it to adjust again. |
| Floating window order (`떠있는 창 앞뒤 순서`) | Choose normal, always-on-top, or always-behind. |

## Settings guide

Run the EXE directly, or right-click the widget and choose **Edit widget settings (`위젯 설정 수정`)**. Hover over controls for more detailed in-app explanations.

### Metric table

| Setting / Korean label | Description |
| --- | --- |
| Show / 표시 | Show or hide the entire metric item. |
| Metric / 항목 | CPU, memory, disk, network, or GPU. |
| Display name / 표시 이름 | Short label shown in the widget. Click the cell to edit it. |
| Temperature / 온도 | Show CPU/GPU temperature between the label and usage value. Toggle each separately. Only the temperature is omitted if its sensor cannot be read. |
| Temperature color / 온도 색 | Choose separate CPU and GPU temperature text colors. The default is orange. |
| Value / 숫자 | Show the current usage or network transfer rate numerically. |
| Value format / 값 형식 | For memory, choose `%`, used GB (`사용 GB`), or used/total GB (`사용/전체 GB`). Other metrics use their default format. |
| Graph / 그래프 | Show a miniature graph covering the selected history window. |
| Graph style / 그래프 형태 | Line (`선`) is the simplest; filled (`채움`) emphasizes changes; bars (`막대`) help compare individual samples. |
| Color / 색상 | Choose the item label and graph color. |

Use **Show all (`전체 표시`)**, **Hide all (`모두 해제`)**, **Enable all graphs (`그래프 전체`)**, and **Disable all graphs (`그래프 해제`)** for bulk changes. **Up (`위로`)** and **Down (`아래로`)** change the item order.

### Size, behavior, and performance

| Setting / Korean label | Range / default or recommendation | Description |
| --- | --- | --- |
| Refresh interval / 갱신 간격 | 200–10000 ms; **1000 ms recommended** | How often measurements are read. Lower values react faster but can increase CPU usage. |
| Graph history / 그래프 기록 | 10–600 seconds; **60 seconds recommended** | History kept in miniature graphs. Longer windows show more history and use somewhat more memory. |
| Maximum width / 최대 너비 | 160–1200 px; default 560 | Maximum widget width. Increase for more content or decrease when taskbar space is limited. |
| Left offset / 왼쪽 여백 | 0–1200 px; default 126 | Starting-position adjustment relative to the taskbar's left edge. Used to fine-tune placement between weather and Start. |
| Opacity / 투명도 | 25–100%; default 94% | Panel opacity in above-taskbar and popup modes. Does not apply to inside mode with transparent taskbar background enabled. |
| Font size / 글꼴 크기 | 7–18; default 9 | Label and value text size. Sizes 8–9 suit narrow columns. |
| Display position / 표시 위치 | Inside / above / popup | Popup is an independent window toggled with the tray icon. |
| Fullscreen behavior / 전체화면 동작 | Hide / always show / click-through | Click-through keeps the widget visible while forwarding mouse input to the fullscreen application. |
| Inside item width / 내부 항목 폭 | 48–180 px; default 70 | Reference width per CPU/RAM/etc. item in inside mode. Narrow labels may be shortened. |
| Inside height / 내부 높이 | 20–48 px; default 28 | Inside-taskbar widget height, automatically limited to the taskbar height. |
| Popup width / 팝업 너비 | 200–1200 px; default 500 | Independent popup width. Adjust for the number of items and desired graph width. |
| Popup height / 팝업 높이 | 48–400 px; default 72 | Independent popup height. Increasing it also gives graphs more space. |
| Floating window order / 떠있는 창 순서 | Normal / always-on-top / always-behind | Normal ordering is the default. Always-on-top is only enabled by explicit selection; always-behind places the widget behind other ordinary windows. |

In popup mode, drag the widget body to the desired position and monitor. Hover over the lower-right diagonal grip for the resize cursor; its surrounding 44 px hit area allows width and height adjustments. Releasing saves the size. After choosing the size and position, enable **Pin current popup position** and apply: coordinates are saved, and movement and resizing lock. The pinned position is restored on restart. Disable pinning to adjust again. When full-widget interaction is off, all mouse input passes through, so enable interaction before moving or resizing.

Popup and above-taskbar modes do not bring themselves forward on every refresh. With normal ordering, clicking another application naturally puts the widget behind it. Always-on-top applies only when explicitly selected. Opening and closing Start or Search does not unhide a manually hidden popup. Startup popup visibility has a separate option.

### Options

| Option / Korean label | Description |
| --- | --- |
| Auto-fit to space / 공간에 자동 맞춤 | Fill the usable space between weather and Start, dividing it equally among enabled items. Disabling items does not reduce the overall allocated width. |
| Measurement while hidden / 숨김 중 측정 | Stop completely (`완전히 중지`, default), sample every five seconds (`5초 간격 절전`), or continue normally (`계속 측정`). Stop completely pauses measurement and graph recording while both the widget and Settings are hidden. |
| Start with Windows / Windows 시작 시 자동 실행 | Start the widget at sign-in using saved settings. |
| Open Settings on manual launch / 직접 실행 시 설정 먼저 표시 | Open Settings first when running the EXE manually. Does not apply to automatic startup. |
| Transparent taskbar background / 작업표시줄 배경 투명 | In inside mode, show only text and graphs. Disable to show the panel background, border, and item separators. |
| Full-widget interaction / 위젯 전체 영역 클릭 인식 | Enable clicks and right-clicks across the widget, including empty space. Disable to pass all input through to the taskbar or window underneath. |
| Left/right overflow pages / 공간 초과 시 좌우 페이지 | Off by default. Enable arrow-button navigation. Some items from the previous page may repeat to fill the last page. |
| Pin current popup position / 현재 팝업 위치 고정 | Save the current popup coordinates and lock movement and resizing. Set size and position before enabling and applying. |
| Show popup at startup / 앱 시작 시 팝업 표시 | On by default. Disable to start in the tray when launching manually or at sign-in; click the tray icon to show the popup. |

Turning full-widget interaction off disables widget clicks, right-clicks, double-clicks, and popup dragging, forwarding them to the window underneath. To re-enable it, right-click the Taskbar Monitor tray icon. The widget context menu closes automatically 1.2 seconds after the pointer leaves the menu and its submenus. Right-clicking the menu again closes it immediately.

Only the Settings content area scrolls. The status message and **Restore defaults (`기본 설정 복원`)**, **Close (`닫기`)**, **Save and apply (`저장·적용`)**, and **Save and turn on widget (`저장하고 위젯 켜기`)** buttons stay fixed at the bottom. Restore defaults loads default values into the form; save them to apply them to the application.

### Disk drive selection

Select multiple available drives, such as C: and E:, under **Disk drives to display (`표시할 디스크 드라이브`)**. Each selected drive receives its own item and graph. If you need to navigate many drives, enable the otherwise-disabled overflow paging option and use `‹` and `›`. Measurements are read together from Windows logical-disk counters; no separate process is launched per drive.

Both save buttons save settings and keep Settings open. **Save and turn on widget** additionally shows a hidden widget. Enter activates **Save and apply**. Close Settings with **Close** or the title-bar X. The live preview uses the same available-taskbar-width calculation as the real widget, so disabling metrics does not independently shrink it.

## Installation and running

### Build from source

1. Download or clone this repository.
2. Open PowerShell in the repository folder.
3. Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The build produces a single `release\TaskbarMonitor.exe`. If you only want to use the widget, download that EXE using the link at the top. JSON, PNG, and PDB test files are not included in the release folder. The build uses the Windows .NET Framework C# compiler; separate Python or .NET SDK installation is not required.

To modify the code, download the entire repository and edit the C# files in `src`. `src/AppSettings.cs` is settings-handling source code, not a personal settings file. Automated tests are in `tests/RegressionTests.cs`. The build compiles both folders without requiring you to move files to the root. The finished EXE runs without the source or test folders.

Exit this folder's running EXE using the tray menu before building. If it is running, the build stops to avoid leaving Windows-generated temporary copies of the previous running image in the release folder. A temporary build folder is used only during compilation. The existing EXE is replaced only after successful compilation; compilation failure preserves it. The build does not automatically delete other user files. Automated tests run separately through `test.ps1`; compilation success alone does not establish release readiness.

### Run

Run `release\TaskbarMonitor.exe`. By default, a manual launch opens Settings first. Administrator privileges are not required.

Personal settings are stored at:

```text
%LOCALAPPDATA%\TaskbarMonitor\settings.json
```

## Performance and privacy

- The recommended refresh interval is 1000 ms. Faster 200–500 ms updates make graphs smoother but can increase CPU usage.
- Sampling runs in a background task from the first measurement, keeping Settings and the tray available during sensor initialization.
- Temperature sensors are read once every two seconds, independently of the usage/graph refresh interval, to reduce sensor-query overhead.
- Network adapter lists are cached. Counters for unselected disks and the GPU are not opened unnecessarily.
- Graph data stays in memory only for the selected time window, with a maximum of 3000 samples per item. Performance history is not continuously written to disk. Error logs also have a size limit.
- Hidden measurement can stop completely, use five-second sampling, or continue normally.
- Measurements and settings storage are local to the PC.
- No network server, account, advertisements, or telemetry are used.
- Error logs are not included in the distribution or GitHub repository and are not automatically transmitted. They are created only in the settings folder on the PC where an error occurs. Records contain UTC time, error type/code, and internal application function names, not raw error messages, file paths, or exception metadata. Distribution checks reject files other than the EXE in the release folder and log files tracked by Git.
- The application is designed to run without administrator privileges.

## Updating, uninstalling, and settings recovery

- **Update:** exit from the tray menu, replace the existing EXE, and run it again. Settings are stored separately and are not reset merely by replacing the EXE. There is no automatic updater.
- **Automatic startup:** keep the EXE at the same location. If you move it, run it from its new location, check **Start with Windows**, and select **Save and apply** to register the new path.
- **Uninstall:** disable automatic startup and save, exit the application, then delete the EXE. To also remove settings and error records, delete `%LOCALAPPDATA%\TaskbarMonitor` separately. Deleting that folder removes your preferences.
- **Unreadable settings:** the application starts with defaults and shows a notice at the bottom of Settings. It preserves the original as one `settings.json.unreadable.bak` file without overwriting an existing backup. Backup failures are also reported in the status area.
- Out-of-range settings, invalid display modes, and duplicate or empty metric entries are normalized at startup. Normal personal preferences are not reset during an update.
- Closing Settings with unsaved changes offers save, discard, or cancel. Unsaved changes are not guaranteed to survive Windows shutdown or forced process termination.

## Temperature support

CPU and GPU temperatures are enabled by default and appear between name and usage, for example `CPU 57°C 25%` and `GPU 51°C 11%`. Toggle each in the **Temperature (`온도`)** column.

- NVIDIA GPU temperatures come directly from NVML in the installed NVIDIA driver. The application does not repeatedly launch `nvidia-smi` or distribute a separate sensor DLL.
- If a sensor temporarily stops responding, the last valid temperature is retained for at most ten seconds, then omitted. NVIDIA query failures release the connection and trigger initialization retries at ten-second intervals.
- CPU readings use Windows ACPI Thermal Zones. Depending on the PC and BIOS, these may differ from CPU package temperature. No CPU temperature appears when the device does not expose an accessible sensor.
- Unavailable temperatures are omitted rather than fixed at `0°`; usage values and graphs continue working.
- GPU temperature currently supports NVIDIA. AMD and Intel GPUs may display usage only.

## Known limitations

- Development and testing currently focus on the standard horizontal Windows 11 taskbar on the primary monitor.
- Changes to taskbar positioning or structure in Windows updates may require placement adjustments.
- GPU usage may display 0% depending on driver and Windows performance-counter support.
- Compatibility with customized taskbars and replacement shells is not guaranteed.
- There is no installer or automatic updater yet.
- CPU temperature is an ACPI reference reading, not a guaranteed CPU package temperature. On multi-GPU systems, the highest GPU usage and highest NVIDIA temperature may come from different devices.
- The EXE is not code-signed. Security settings and file reputation may cause warnings or blocking. Disabling security protections is not required or recommended by this project.

## Development checks

After modifying the source, build and run hardware-independent regression tests:

```powershell
powershell -ExecutionPolicy Bypass -File .\test.ps1
```

The 22 regression checks cover text layout and pixel boundaries, data refresh in all three display modes, sensor expiration/recovery, taskbar space calculation, graph sample limits and time axes, defaults and invalid settings, hidden measurement, click-through, final-page filling, popup size limits, the fixed Settings footer and its minimum width, and exclusion of private paths/messages from error records. Additional checks validate the EXE-only release folder, exclusion of tracked logs, execution privileges, and Windows compatibility declarations. Result JSON is written only to a temporary folder without manipulating personal settings or the running widget. GitHub Actions tests both the committed EXE and an EXE freshly built from source.

Passing automated tests does not guarantee correct operation on every PC. Real-device checks are still needed for context-menu dismissal, dropdown stability across refreshes, display-mode switching, popup movement/pinning/resizing/window ordering, double-click behavior while click-through is active, sleep/resume, and multiple monitors.

## License

This project is distributed under the [MIT License](LICENSE). Free use, studying and modifying the code, redistribution, and commercial use are permitted subject to retaining the copyright and license notice. The software is provided without warranty. The full license is included in the repository and embedded in the EXE.

## Repository and distribution layout

The local project and GitHub repository use this layout, including `release/TaskbarMonitor.exe`. Users who only want to run the application need just the EXE. Git metadata, temporary build files, personal settings, and error records are not uploaded as project files.

```text
Local project / GitHub repository
├─ release/
│  └─ TaskbarMonitor.exe   Single executable to run or share
├─ src/                   Application source
│  ├─ *.cs                Settings UI, measurements, widget, etc.
│  └─ app.manifest        Windows execution and compatibility manifest
├─ tests/
│  └─ RegressionTests.cs  Automated tests
├─ docs/                  README screenshots
├─ .github/workflows/     GitHub build and test configuration
├─ build.ps1              EXE build script
├─ test.ps1               Regression test script
├─ .gitignore             Exclusions for temporary files and personal records
├─ LICENSE               MIT License
├─ README.md              Korean documentation
└─ README.en.md           English documentation
```

`src/app.manifest` is embedded during the build and does not need to be downloaded separately to run the application. It does not require edits on every update. The GitHub workflow invokes `test.ps1` to build and test changes; it is not needed to run the widget on a user's PC. Source and tests are for contributors, while `docs` and the READMEs explain usage. Personal settings and internal `.git` management data are not meant to match the GitHub file listing.
