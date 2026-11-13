# Temp Auto Cleaner

[简体中文](README.zh-CN.md)

Temp Auto Cleaner is a Windows utility that removes eligible temporary files for the current user and exits after one pass. V0.1.0-alpha provides the cleanup core. It runs without a visible console window or administrator privileges and makes no network requests.

## Cleanup rules

- The target is `%LOCALAPPDATA%\Temp`, resolved through Windows for the current user. The executable accepts a single optional `--dry-run` argument.
- Both creation time and last-write time must be strictly older than 24 hours. The cleaner skips files with uncertain metadata, protected attributes, multiple hard links, named NTFS data streams, or conflicting open handles.
- The program traverses ordinary directories. It skips symbolic links, junctions, and other reparse points, and keeps the Temp root. Empty subdirectories are considered after their files and must still meet both age checks.
- The alpha supports a local NTFS target. Missing or unsafe roots cause the program to exit.

## Build and preview

Build on x64 Windows using the .NET SDK and the .NET Framework 4.8 Developer Pack. The application runs on .NET Framework 4.8.

```powershell
dotnet build .\src\TempAutoCleaner\TempAutoCleaner.csproj -c Release
$exe = Resolve-Path .\src\TempAutoCleaner\bin\Release\net48\TempAutoCleaner.exe
$run = Start-Process -FilePath $exe -ArgumentList '--dry-run' -Wait -PassThru
$run.ExitCode
```

Dry Run leaves the cleanup target intact and writes a report under `%LOCALAPPDATA%\TempAutoCleaner\logs`. Review that report before running the executable without arguments to perform a cleanup. Reports are JSON with UTC timestamps and omit personal file paths. Regular cleanup expires logs older than 30 days. Reported bytes estimate the sizes of deleted files and can differ from the change in available disk space.

Exit codes are `0` for success, `1` for partial success, `2` for failure, `3` when another instance is already running, and `64` for invalid arguments.

## Development status

The installer, login task, and packaged releases are planned for later milestones. Full Windows 10 22H2 and Windows 11 compatibility acceptance is pending.

See [contributing and isolated tests](CONTRIBUTING.md), the [technical design](docs/technical-design.md), and the [MIT license](LICENSE).
