# Alpha 技术设计

见 [产品需求与开发规范](requirements.md)。

## 入口和清理边界

应用以 .NET Framework 4.8 为目标，构建为 x64 Windows EXE。应用入口解析当前用户的 `LocalApplicationData`，并以其 `Temp` 子目录作为唯一清理目标。测试程序集可以将自己创建的隔离目录交给核心，以复用实际清理逻辑。

清理核心验证目标位于本地 NTFS 卷，从卷根开始逐段打开并检查目录。扫描时以已经验证的父目录句柄为基准打开单个子项，拒绝重解析点。目录句柄在遍历期间保持打开，并禁止删除共享，以限制路径上目录的移动或改名。应用仅对打开的对象句柄申请删除，扫描结果中的名称用于定位候选项。

对象打开后重新检查类型、文件 ID、时间和属性。遇到候选项被替换、文件正在使用、权限不足或路径消失时跳过。路径安全检查失败会终止对应范围内的清理。普通文件的创建时间和最后修改时间都必须严格早于本轮开始时间减去 24 小时。多硬链接、带受保护属性和含 NTFS 命名数据流的文件会被跳过。

子目录以自底向上的顺序处理。发现可能符合条件的空目录后，程序会以独占方式重新打开该对象，复核文件 ID、空目录状态和两个时间戳。目录即使没有子项，也可能带有 NTFS 命名数据流。程序用删除句柄检查数据流，发现数据流或无法确认时保留该目录。删除其中的文件可能刷新目录的最后修改时间，目录会在这种情况下保留。目录遍历过程中出现的新内容由文件系统的空目录删除规则保护。Temp 根目录始终保留。扫描深度超过 256 层的目录会跳过并记录错误。

Dry Run 运行候选项判断并记录报告。它保留清理目标，Windows 仍可能按卷配置更新最后访问时间。由于预演保留现有子项，而实际删除可能改变目录时间和内容，目录结果可能不同。文件能否删除还取决于应用实际执行时的占用状态和文件系统返回结果。

Windows 的共享模式约束数据读写和删除访问，属性专用访问仍可并发。清理程序在提交删除前重读元数据，检查与提交是两次系统调用。[Windows 文件共享规则](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)

相关系统接口见 [OBJECT_ATTRIBUTES](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wudfwdm/ns-wudfwdm-_object_attributes)、[目录枚举结构](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_both_dir_info)和[句柄删除接口](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle)。

## 日志和验收

每轮清理在 `%LOCALAPPDATA%\TempAutoCleaner\logs` 生成一份 JSON 报告。报告先写入同一目录中的临时文件，完整写入并刷新后才以 JSON 文件名发布。正式清理轮换超过 30 天的程序报告和残留临时报告。Dry Run 只写入本次报告。

| 字段 | 含义 |
| --- | --- |
| `started_utc`, `finished_utc` | 运行开始和结束时间，统一为 UTC |
| `dry_run` | 是否为预演 |
| `result` | `success`、`partial_success` 或 `failed` |
| `deleted_files`, `skipped_files`, `deleted_directories`, `skipped_directories` | 删除和跳过的对象计数 |
| `estimated_deleted_bytes` | 已删除文件的大小估算 |
| `eligible_files`, `eligible_bytes`, `eligible_directories` | 通过候选项判断的文件数、文件大小和目录数 |
| `errors`, `errors_truncated` | 仅包含操作标识与错误代码的记录，以及截断标记 |

自动化测试使用调用者显式指定的隔离目录。测试覆盖时间阈值、占用、重解析点、硬链接、目录处理、对象替换和 Dry Run。使用测试目录外的哨兵验证清理边界。

## 安装、升级和卸载

安装包为当前用户安装到本地应用目录。安装和卸载会获取清理程序使用的同一个用户互斥锁。如果清理正在运行，用户可以等待后重试。安装器创建并复核按用户 SID 命名的计划任务，任务只在该用户登录后延迟 30 秒启动，使用普通权限并忽略重叠触发。安装器只注册任务，首次清理由下一次登录触发。

升级前保存原有任务定义。安装在完成前失败时，尝试移除新任务或恢复原任务。卸载先禁用并删除本应用的任务，再移除安装文件，保留本地日志。便携 ZIP 直接使用同一份清理 EXE。

V1.0.0 仍需完成 Windows 10 22H2 x64 与 Windows 11 x64 的实际登录和安装场景，以及 Unicode 和中英文系统验收。稳定版通过关键安全测试后由人工确认发布，并附中英文版本说明。
