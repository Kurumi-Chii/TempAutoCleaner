# Temp Auto Cleaner

[English](README.md)

Temp Auto Cleaner 是一款 Windows 工具，用于清理当前用户符合条件的临时文件。每次只运行一轮，完成后自动退出。程序无可见控制台窗口，无需管理员权限，也不发起网络请求。

## 安装版与便携版

- **安装版**仅为当前用户安装。计划任务从下次登录起延迟 30 秒运行。通过 Windows 设置卸载可移除任务和程序文件，清理日志会保留。
- **便携版**解压后启动 `TempAutoCleaner.exe` 即清理一轮。可用 `--dry-run` 预演。便携版不会注册计划任务。
- 需要 x64 Windows 和 .NET Framework 4.8 或更新版本。Windows 10 22H2 和 Windows 11 的完整兼容性验收仍待完成。

## 清理规则

- 清理目标固定为 Windows 为当前用户解析的 `%LOCALAPPDATA%\Temp`。程序只接受一个可选参数 `--dry-run`。
- 创建时间和最后修改时间都必须严格超过 24 小时。程序会跳过元数据无法确认、带受保护属性、有多个硬链接、含 NTFS 命名数据流或存在冲突占用的文件。
- 程序扫描普通子目录，跳过符号链接、目录联接点和其他重解析点，保留 Temp 根目录。子目录在处理完其中的文件后，只有为空且两个时间戳仍符合条件时才会尝试删除。
- 清理目标须位于本地 NTFS 卷。根目录缺失或路径无法验证时，程序会退出。

## 构建和预演

构建需要 x64 Windows、.NET SDK 和 .NET Framework 4.8 Developer Pack。运行程序需要 .NET Framework 4.8。

```powershell
dotnet build .\src\TempAutoCleaner\TempAutoCleaner.csproj -c Release
$exe = Resolve-Path .\src\TempAutoCleaner\bin\Release\net48\TempAutoCleaner.exe
$run = Start-Process -FilePath $exe -ArgumentList '--dry-run' -Wait -PassThru
$run.ExitCode
```

Dry Run 会保留目标中的文件和目录，并把报告写入 `%LOCALAPPDATA%\TempAutoCleaner\logs`。可先查看报告，再无参数启动程序以执行实际清理。报告使用 JSON，时间统一为 UTC，不含个人文件路径。实际清理会轮换超过 30 天的日志。报告中的清理字节数按被删除文件的大小估算，可能与磁盘可用空间的变化不同。

退出码 `0` 表示成功，`1` 表示部分成功，`2` 表示失败，`3` 表示已有实例运行，`64` 表示参数无效。

参见[构建与隔离测试](CONTRIBUTING.md)、[技术设计](docs/technical-design.md)和 [MIT 许可证](LICENSE)。
