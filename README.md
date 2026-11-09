# Power Plan Switcher

A Windows tray app that switches the active power plan when you go idle, come
back, or unplug the charger.

## Download

Take `PowerPlanSwitcher.exe` from [Releases](../../releases). Install the
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) for
Windows x64 first, then run the exe. It stays in the notification area.

## Screenshots

<p align="center">
  <img src="docs/images/settings-top.png" width="300" alt="Settings window showing status and the plan matrix">
  <img src="docs/images/settings-bottom.png" width="300" alt="Settings window showing idle detection, scanning and startup">
</p>
<p align="center">
  <img src="docs/images/tray-menu.png" width="260" alt="Tray menu with Auto and Force options">
</p>

## What it does

- Watches input idle time (`GetLastInputInfo`), the screensaver
  (`SystemParametersInfo`), lock and unlock (`SystemEvents.SessionSwitch`), and
  the power source (`GetSystemPowerStatus`).
- Maps the four combinations of activity and power source to existing plans. By
  default, active on AC uses Balanced and the other three use Power saver.
- Auto mode applies the rules. The tray Force menu pins Power saver, Balanced,
  or High performance for the current session without saving it.
- Reads the active plan with `PowerGetActiveScheme`, switches with
  `PowerSetActiveScheme`, then reads it back to confirm. A missing plan or an
  unset cell leaves the current plan alone.

## Requirements

- Windows 10 or 11 on x64.
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) to
  run the release build. Newer desktop runtimes work too.
- To build: .NET 8 SDK. The app targets `net8.0-windows` and rolls forward to
  newer desktop runtimes.

## Build from source

```powershell
dotnet build powerplan.slnx
dotnet test powerplan.slnx
dotnet publish src/PowerPlanSwitcher/PowerPlanSwitcher.csproj -c Release
```

Publish writes one exe. Add `-p:SelfContained=true` for a portable build that
bundles the runtime.

## Configuration

Settings live in `%LocalAppData%\PowerPlanSwitcher\settings.json`. Writes go
through a temp file and `File.Replace`, so a crash does not leave a half-written
file.

```json
{
  "version": 1,
  "rules": {
    "acActive": "381b4222-f694-41f0-9685-ff5bb260df2e",
    "batteryActive": "a1841308-3541-4fab-bc81-f71556f20b4a",
    "acIdle": "a1841308-3541-4fab-bc81-f71556f20b4a",
    "batteryIdle": "a1841308-3541-4fab-bc81-f71556f20b4a"
  },
  "idle": { "inputTimeoutMinutes": 5, "onScreensaver": true, "onLock": true },
  "scanIntervalSeconds": 2,
  "startWithWindows": true
}
```

The app also accepts the nested shape (`rules.active.ac`, `rules.idle.battery`).
A bad GUID marks that cell unset instead of resetting the file. A corrupt file
is renamed to `settings.json.bad`. Fields the app does not know survive a round
trip. Idle time clamps to 1 to 120 minutes and the scan interval to 1 to 60
seconds.

The log is `%LocalAppData%\PowerPlanSwitcher\app.log`. It rolls at 1 MB and
keeps one previous file.

## Manual checklist

These tests need Windows 10 or 11 with both AC and battery available.

1. Leave the PC idle past the timeout. The plan changes to Power saver.
2. Move the mouse. It changes back within two seconds.
3. Start the screensaver. The change is immediate.
4. Press Win+L, then unlock. The plan changes in both directions.
5. Unplug and replug the charger. The plan follows the battery and AC cells.
6. Pick a plan from Force, restart the app, then check that it returns to Auto.
7. Delete a plan the app points at. The cell shows "Not set" and the app keeps
   running.
8. Toggle Start with Windows. Check the Run key.
9. Launch the app twice. One tray icon appears and the second process exits.
10. Corrupt `settings.json`. The app starts with defaults and writes
    `settings.json.bad`.

## License

GPL-3.0, see [LICENSE](LICENSE). Fork of PowerPlanManager.
