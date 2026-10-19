# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning: [SemVer](https://semver.org/).

## [1.0.0] - 2026-10-19

### Added
- First public portfolio release.
- Folder pairs (source to destination) backed up into timestamped folders, written as `.partial` and renamed only when the copy has finished.
- Locked or unreadable files retried once, then skipped and logged; such backups are kept as `_incomplete`, cancelled or failed ones are removed.
- Retention of the last N backups per pair, limited to folders carrying the pair's `.boh-backup.json` marker.
- Network fallback folder when the destination does not answer within the timeout.
- Daily, weekly or monthly schedule through the Windows Task Scheduler, optionally also after sign-in, running without administrator rights.
- Silent `--run-backup` mode with a log file and exit codes 0, 1 and 2.
- Portable mode with `boh_config.json` next to `BOH.exe`, otherwise settings in `%APPDATA%\BOH`.
- WPF window with a live log, dark theme, keyboard shortcuts and an About card.
- xUnit tests, including an off-screen render of the main window with demo data.
