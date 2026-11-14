# Changelog

## 0.9.0-beta (in development)

- Add a bilingual per-user installer and a 30-second delayed sign-in task.
- Package the portable executable and generate SHA-256 checksums.

## 0.1.0-alpha

- Add a single-pass Windows cleanup core for the current user's Local AppData Temp directory.
- Check both file timestamps and use native file handles to limit traversal and deletion to the validated directory tree.
- Add Dry Run, structured local reports, and report retention.
- Add a Windows test harness that requires an explicit isolated test location.
- Preserve empty directories with NTFS named data streams in both cleanup and Dry Run.
- Test the single-instance mutex across processes and run the isolated suite in Windows CI with zero skips required.
