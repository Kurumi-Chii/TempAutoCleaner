# Temp Auto Cleaner

[简体中文](README.zh-CN.md)

Temp Auto Cleaner is a Windows utility that removes eligible temporary files for the current user and exits after one pass. It runs without a visible console window or administrator privileges and makes no network requests.

## Installation and portable use

- **Setup** installs for the current user and registers a task that runs 30 seconds after the next sign-in. Uninstall from Windows Settings to remove the task and application files. Cleanup logs are kept.
- **Portable** runs once when you start `TempAutoCleaner.exe`. Use `--dry-run` for a preview. Portable use does not register a scheduled task.
- Use x64 Windows with .NET Framework 4.8 or later. Full Windows 10 22H2 and Windows 11 acceptance is pending.

## Cleanup rules

- The target is `%LOCALAPPDATA%\Temp`, resolved through Windows for the current user. The executable accepts one optional argument, either `--dry-run` or `--inspect-large-files`.
- Both creation time and last-write time must be strictly older than 24 hours. The cleaner skips files with uncertain metadata, protected attributes, multiple hard links, named NTFS data streams, or conflicting open handles.
- The program traverses ordinary directories. It skips symbolic links, junctions, and other reparse points, and keeps the Temp root. Empty subdirectories are considered after their files and must still meet both age checks.
- The cleaner supports a local NTFS target. Missing or unsafe roots cause the program to exit.
- **Why are files still in Temp after cleanup?** The cleaner keeps the Temp root and checks each file individually. It leaves recently created or modified files, files in use, and protected files in place while removing eligible older files in the same folder. A subfolder stays if it still contains files or subfolders.

## Build and preview

Build on x64 Windows using the .NET SDK and the .NET Framework 4.8 Developer Pack. The application runs on .NET Framework 4.8.

```powershell
dotnet build .\src\TempAutoCleaner\TempAutoCleaner.csproj -c Release
$exe = Resolve-Path .\src\TempAutoCleaner\bin\Release\net48\TempAutoCleaner.exe
$run = Start-Process -FilePath $exe -ArgumentList '--dry-run' -Wait -PassThru
$run.ExitCode
```

Dry Run leaves the cleanup target intact and writes a report under `%LOCALAPPDATA%\TempAutoCleaner\logs`. Review that report before running the executable without arguments to perform a cleanup. Cleanup and Dry Run reports are JSON with UTC timestamps and omit personal file paths. Regular cleanup expires logs older than 30 days. Reported bytes estimate the sizes of deleted files and can differ from the change in available disk space.

Exit codes are `0` for success, `1` for partial success, `2` for failure, `3` when another instance is already running, and `64` for invalid arguments.

## Inspect large files

Run this check manually with the same `$exe` as above.

```powershell
$run = Start-Process -FilePath $exe -ArgumentList '--inspect-large-files' -Wait -PassThru
$run.ExitCode
```

The inspection reads file metadata. It reports verifiable ordinary files at least 100 MiB in size that were created more than 48 hours ago and modified within the last 24 hours, measured from the start of the scan. The local JSON report in `%LOCALAPPDATA%\TempAutoCleaner\logs` shows the match count in `large_recent_files.total` and up to the 20 largest matches in `large_recent_files.files`.

Only the manual inspection report includes paths relative to Temp. Redact them before sharing the report. The inspection leaves Temp files and existing logs in place.

See [build and test instructions](CONTRIBUTING.md), the [technical design](docs/technical-design.md), and the [MIT license](LICENSE).
