# Changelog

## 0.3.1

- Settings window polish: the window sits on a subtle gradient backdrop
  instead of flat color, the rules grid has an anchored header with a band
  behind it, the status line has an idle/active dot, the mode pill is tinted
  when forced or paused, the empty Recent Changes card hides itself, and the
  GENERAL rows share one layout.
- The tray menu header stays short (plan plus mode); the detail moved to the
  tooltip. Disabled tray text is brighter.

## 0.3.0

- The app checks GitHub once a day for a newer release, on startup and every
  24 hours while running. A toggle under General turns the check off; it is
  on by default.
- When a newer release is out, the tray menu and the settings status card
  offer Update. The download is checked against the published SHA-256 before
  anything is replaced, the exe is swapped, and the app restarts.
- If the exe sits somewhere the app cannot write, the release page opens
  instead. A failed check or download is shown in the status card and tried
  again the next day.
- The last check time lives in `update.json` next to the settings file; the
  settings file gains only `checkForUpdates`.

## 0.2.1

- The tray menu follows a light or dark switch made while the app runs, and
  uses system colors under high contrast.
- A settings window that fails mid-open is closed and cleaned up instead of
  lingering hidden beside a new one.
- The settings file name and the plan-change key each live in one place.
- The README documents the startup check on the Start with Windows entry and
  lists the full tray menu.

## 0.2.0

- A wrong-typed settings field now falls back to its default and is named in the
  problem message. The rest of the file still loads. A bad rule cell is unset
  and reported.
- Unknown fields in the root, rules, and idle sections are kept when the settings
  are saved.
- A settings file written by a newer schema loads with a warning.
- Save failures show in the status card. A startup failure shows an error message
  instead of leaving a hidden process.
- The status card shows problems, the current mode, the last check time, and the
  five most recent switches.
- Pause for 1 hour or until restart is available from the tray and the settings
  window.
- Force lists every plan on the PC, including custom plans. While a plan is
  forced, the rules grid is disabled and Return to automatic sits in the status
  card.
- A plan that is no longer installed shows as Missing plan. A failed power API
  call is reported as a read failure, not a deleted plan.
- An unknown power source leaves the active plan alone.
- Logs are one file per day under `logs`, kept for 14 days. Each day's file
  rolls at 5 MB and keeps one `.1` part. The settings window has a log pane with a
  date picker, live refresh, open file, open folder, and copy.
- Copy diagnostics writes the version, plan list, paths, and recent log lines to
  the clipboard.
- The window follows light, dark, and high contrast themes. ComboBox, TextBox,
  Button, CheckBox, ScrollBar, and the title bar are themed. The plan pickers,
  timeout field, checkboxes, and log controls have accessible names, the main
  buttons have access keys, and keyboard focus is visible. DPI is per-monitor.
- New installs start with Start with Windows off. Existing settings are
  unchanged.
- The app has an icon, a version line in settings, and a one-time tray hint on
  first run.
- Building needs SDK 9.0.200 or newer because the solution is `.slnx`. Release
  tags now set the file version.
- New tests cover config parsing, daily log files, app state, and the settings
  view model.

## 0.1.0

First public version.
