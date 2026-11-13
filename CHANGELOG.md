# Changelog

## 0.1.0-alpha (in development)

- Add a single-pass Windows cleanup core for the current user's Local AppData Temp directory.
- Check both file timestamps and use native file handles to limit traversal and deletion to the validated directory tree.
- Add Dry Run, structured local reports, and report retention.
- Add a Windows test harness that requires an explicit isolated test location.
