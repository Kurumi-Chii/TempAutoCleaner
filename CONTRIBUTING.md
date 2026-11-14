# Contributing

Build on Windows x64 with the .NET SDK and .NET Framework 4.8 Developer Pack. Keep the cleanup rules independent of future installer and Task Scheduler code.

## Build packages

Install Inno Setup 6.7.4 and place its matching `ChineseSimplified.isl` in the compiler's `Languages` directory. Run `installer/Build-Packages.ps1` with `-InnoCompiler` pointing to `ISCC.exe`. The setup executable, portable ZIP and SHA-256 checksums are written to `dist/`. [Compiler release](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_4) · [Simplified Chinese translation](https://github.com/jrsoftware/issrc/blob/is-6_7_4/Files/Languages/Unofficial/ChineseSimplified.isl)

## Isolated tests

From the repository root, build the test executable and provide an explicit local NTFS parent directory for its fixtures. Use a dedicated workspace directory for this purpose.

```powershell
dotnet build .\tests\TempAutoCleaner.Tests\TempAutoCleaner.Tests.csproj -c Release
$sandbox = (New-Item -ItemType Directory -Force -Path .\test-sandbox).FullName
& .\tests\TempAutoCleaner.Tests\bin\Release\net48\TempAutoCleaner.Tests.exe $sandbox
if ($LASTEXITCODE -ne 0) { throw "Cleanup tests failed with exit code $LASTEXITCODE" }
```

The harness creates its own fixture directories below that parent. Exercise the application against these fixtures through the test harness. Use separate fixture and sentinel directories when checking traversal boundaries. Preserve fixture isolation when adding tests for access failures, links, and changes between enumeration and deletion.

Pass `--strict` before the sandbox path to fail the run when any fixture is skipped. Windows CI uses this mode. The cross-process test launches the test executable and exercises the same mutex logic used at startup with a dedicated mutex and a callback that only returns an exit code.

Before requesting review, report the Windows edition and build, test results, and any cases blocked by filesystem capabilities or permissions. Include the expected safety behavior when changing path validation, time checks, or deletion.
