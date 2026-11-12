# Update check and self-update — design

Date: 2026-11-11
Status: approved

## Why

People who download the exe from Releases have no way to learn a new version
exists. The app should notice on its own and update itself in one click.

## Goals

- Check GitHub for a newer release once a day: on startup, then every 24
  hours while running.
- One click ("Update to X") downloads the new exe, verifies it, swaps it in,
  and restarts the app.
- A visible on/off toggle in settings, on by default.
- Failures are quiet: shown in the status card, retried the next day.

## Non-goals

- Silent installs without a click.
- Delta or partial downloads.
- Proxy configuration.
- winget or installer-based distribution.

## Constraints

- A running exe cannot overwrite itself. The swap: rename the running exe to
  `.old`, move the new one into its place, start it, exit. Delete the `.old`
  file on the next start.
- An unwritable exe location (for example Program Files): fall back to
  opening the Releases page and say so in the status card.
- The only network contact is api.github.com (latest release query) and the
  release asset download on GitHub. Nothing else is sent. No telemetry.
- The exe is a single framework-dependent file, so the swap moves one file
  and checks it against the published `.sha256` asset.

## Approaches considered

- In-house updater (picked). A small amount of code, no new dependencies,
  and it matches the existing release shape: an exe plus a sha256 asset per
  release.
- A ready-made updater framework (rejected). It needs installer-based
  releases and changes what people download today.
- Browser-only update notice (rejected). The point is a one-click update
  without manual steps.

## Decisions

- New setting `checkForUpdates` (default true) in settings.json, shown under
  General in the settings window and documented in the README config section.
- The last-check timestamp lives in its own file, `update.json`, next to
  settings.json, so the settings file stays user-owned.
- The update prompt appears as a tray menu item ("Update to X") and a status
  line in the settings window; one balloon tip per release, not per check.
- Version compare: the release tag (`vX.Y.Z`) against the running version.
  Anything that does not parse is skipped. A release must carry both
  `PowerPlanSwitcher.exe` and `PowerPlanSwitcher.exe.sha256` assets or it is
  ignored.
- The download is verified against the published SHA-256 before the swap; a
  bad hash cancels the update and reports it.
- The README privacy section is reworded: the app contacts GitHub once a day
  to check for updates, and the toggle turns that off.
- Ships as 0.3.0 with a CHANGELOG entry. Version compare, check timing, and
  hash verification live in the Core project with unit tests.

## Left to the builder

- Exact placement of the update row in the settings window.
- How download progress is shown (likely the status line and tray text).
