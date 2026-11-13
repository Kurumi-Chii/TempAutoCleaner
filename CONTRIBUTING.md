# Contributing

Build on Windows x64 with the .NET SDK and .NET Framework 4.8 Developer Pack. Keep the cleanup rules independent of future installer and Task Scheduler code.

## Isolated tests

From the repository root, build the test executable and provide an explicit local NTFS parent directory for its fixtures. Use a dedicated workspace directory for this purpose.

```powershell
dotnet build .\tests\TempAutoCleaner.Tests\TempAutoCleaner.Tests.csproj -c Release
$sandbox = (New-Item -ItemType Directory -Force -Path .\test-sandbox).FullName
& .\tests\TempAutoCleaner.Tests\bin\Release\net48\TempAutoCleaner.Tests.exe $sandbox
if ($LASTEXITCODE -ne 0) { throw "Cleanup tests failed with exit code $LASTEXITCODE" }
```

The harness creates its own fixture directories below that parent. Exercise the application against these fixtures through the test harness. Use separate fixture and sentinel directories when checking traversal boundaries. Preserve fixture isolation when adding tests for access failures, links, and changes between enumeration and deletion.

Before requesting review, report the Windows edition and build, test results, and any cases blocked by filesystem capabilities or permissions. Include the expected safety behavior when changing path validation, time checks, or deletion.
