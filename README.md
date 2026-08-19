# WorkTimeTray

Tray app that logs work time to a plain csv. The clock runs while the Windows session is unlocked
and somebody is actually at the machine; locking, sleeping, shutting down, or 10 minutes without
keyboard or mouse input stops it.

## Log

`worktime-YYYY.csv`, one file per year, one line per session:

```
start,stop,hours
2026-08-19 08:07:22,2026-08-19 09:15:41,1.14
```

Local time, sorted by start, `.` as decimal separator. `hours` is derived — the app always recomputes
it from the timestamps, so hand-edited files stay consistent. Unparsable lines and lines starting
with `#` are ignored. The session running right now is added when it ends.

## Window

Click the tray icon.

* Month calendar with hours per day, ISO week sums and a month total; today updates live.
* Each day and week also shows its balance against the expected time, green over / red under.
* Header shows *Working since* the start of the working day — the first logged start of today, not of
  the session that happens to be open — and the time worked today.
* Bottom bar: today, week and month as *worked / expected*, the month balance, and what is left of
  the month target.
* Right panel lists the selected day's sessions, with **Add**, **Edit** and **Delete**. The running
  session is green and cannot be edited until it closes.
* Sessions crossing midnight are split across both days and marked `(+1 d)` / `(-1 d)`.

Keys: arrows move a day, PgUp/PgDn a month, `Home` jumps to today, `Enter` adds, `Del` deletes, `F5`
reloads, `Esc` hides. X hides to the tray; *Exit* in the tray menu really quits.

The window is English whatever the Windows region is set to. The Add/Edit dialog uses spinners rather
than a dropdown calendar, because that calendar is a native control that always follows the region.

## Expected time

A working day expects `ExpectedHoursPerDay`, every other day expects nothing. With flexible hours the
number that matters is the month **balance** — worked minus expected.

* Days in the future are not due yet and never count as a deficit; the full month target is shown
  separately, with the working days left to reach it.
* Days before the log begins expect nothing, so the first month does not open in the red.
* Holidays and vacation are not modelled: a day off shows as a deficit to even out elsewhere in the
  month, or you can add an entry by hand.

## Install

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Publishes to `%LOCALAPPDATA%\Programs\WorkTimeTray`, writes the settings, registers autostart and
starts it. Options: `-LogDirectory <path>`, `-NoAutostart`, `-NoStart`. `.\uninstall.ps1` removes
everything except the csv files.

Autostart registers the `Run` key **and** a Startup folder shortcut, because Explorer was once seen
silently skipping the `Run` entry while starting every other one. Duplicate launches exit quietly,
and every launch appends a line to `worktimetray.log`, which is how you tell whether Windows started
the app. `WorkTimeTray.exe --autostart-on` / `--autostart-off` toggle it from a script, as does the
checkbox in the window.

## Settings

`%LOCALAPPDATA%\WorkTimeTray\settings.json` — a `settings.json` next to the exe wins, and
`%WORKTIMETRAY_DIR%` overrides both. Restart the app after editing.

| Key | Default | Meaning |
| --- | --- | --- |
| `LogDirectory` | the install script's folder | where the csv files go |
| `MinSessionSeconds` | 30 | drop sessions shorter than this; they would round to 0.00 h |
| `IdleTimeoutMinutes` | 10 | pause after this long without input; 0 = never pause on idleness |
| `ExpectedHoursPerDay` | 5.6 | what a working day is expected to be |
| `WorkDays` | Monday–Friday | the days carrying that expectation |
| `WeekStartsMonday` | true | false = follow the Windows locale |
| `ShowWindowOnStartup` | false | open the window at logon too |

The heartbeat file and `worktimetray.log` live next to the settings.

## How it decides you are working

* Session events — lock, unlock, logoff, connect, disconnect, suspend — start and stop the clock on
  the exact second.
* A known lock is only lifted by an unlock event or by fresh input on our own desktop. Windows
  dismisses the lock screen once the display sleeps, so asking "which desktop has input" would report
  a locked machine as unlocked; that alone once logged a whole night as work.
* Polling catches a missed lock within a second. It must hold for 3 seconds, so a UAC prompt does not
  split the day, and the stop is backdated to the first sighting.
* The first session after launch waits for real input, because a process starting on an already
  locked machine cannot tell that from an unlocked one.
* Idle past the timeout stops the clock, backdated to the last keypress; the next input starts a new
  session.
* Sleep, hibernation and crashes never count: gaps are excluded, and a heartbeat every 30 s means a
  power cut costs at most 30 seconds.
* Limits: a session spanning a daylight saving change is off by the shift, and watching a video
  without touching anything counts as a break.

## Build

.NET 8 SDK, `dotnet build src/WorkTimeTray`. `install.ps1` publishes a framework-dependent single
file exe, and clears `MSBuildSDKsPath` first: a stale value left in the environment points at an
SDK version that may no longer exist, which breaks SDK resolution.

## License

MIT, see [LICENSE](LICENSE).
