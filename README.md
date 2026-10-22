# OhMyLockScreen

最小 UWP 自定义锁屏：显示现有的 **It Works!** 页面，点击 **Unlock** 请求 Windows 进入系统解锁流程。有 PIN/密码要求时，仍由 Windows 验证。

窗口启用 `IXamlSourceTransparency.IsBackgroundTransparent`，根 Frame 和页面背景设为 `Transparent`。UWP 窗口底层透明效果依赖系统宿主及全屏模式，锁屏中的实际显示效果需在目标系统确认。

使用附件分析中的 `windows.lockScreen` 扩展及当前用户 `LockAppAumId` 选择机制。该替换机制尚需实机验证，构建成功不代表锁屏托管已经通过验收。

## 构建与手动部署

在 Visual Studio Developer PowerShell 中，于项目根目录运行（需要 .NET 10 SDK、UWP/XAML 构建工具及对应 Windows SDK）：

```powershell
msbuild .\OhMyLockScreen\OhMyLockScreen.csproj /restore /t:Build /p:Configuration=Debug /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true /p:AppxAutoIncrementPackageRevision=false
```

继续使用项目现有的开发签名配置。开发部署可在 Visual Studio 选择 **Debug / x64**，执行 **部署解决方案**；或者在信任开发签名证书后，手动安装本次构建生成的包：

```powershell
Add-AppxPackage -Path '<本次构建生成的 x64 .msix 的完整路径>'
```

从开始菜单普通启动时可预览页面。Unlock 是否能发出解锁请求取决于当前视图能否取得系统的 `LockApplicationHost`，不依赖启动回调类型或手动标记。

页面加载、窗口重新激活或重新可见时会重新查询当前视图的锁屏宿主。首次查询失败后每 250 毫秒重试，最多追加 8 次；之后仍可点击 Unlock 手动重试。查询失败会显示真实 HRESULT 和错误信息，返回空宿主时提示尚未获得宿主，不会直接把它归类为桌面启动。

从 1.0.3.0 开始，公共 `GetForCurrentView()` 返回空值时，会尝试通过它内部使用的 `lockframework.LockFrameworkBroker` 创建宿主，以避开公共工厂缓存 broker 不可用状态的分支。该兼容路径仍由系统校验调用方与当前锁屏视图，不添加能力或绕过凭据验证。它使用未公开的 WinRT 接口，已核对本机 26100 系统组件的接口布局，Windows 10 19044 上的实际效果需要单独验证；激活或调用失败时会显示对应阶段及 HRESULT。

页面下方显示系统提供的 `ViewId`、`IsHosted`、`IsMain`、`IsOnLockScreen`，用于排查视图是否已登记为锁屏视图。**Exit** 会停止当前页面的宿主重试、取消窗口事件订阅，然后调用 `CoreApplication.Exit()` 直接退出应用。此按钮不主动请求解锁，也不恢复锁屏注册表设置；退出后锁屏如何处理由 Windows 决定。

## 启用与恢复

在目标用户的 Windows PowerShell 中运行。脚本只查询当前用户的包、修改当前用户的锁屏选择，不安装包、不触发锁屏。

```powershell
# 先检查包和声明，预览操作，不写设置或备份
.\scripts\LockScreenProvider.ps1 -Action Enable -WhatIf

# 备份原设置并启用
.\scripts\LockScreenProvider.ps1 -Action Enable

# 恢复原设置
.\scripts\LockScreenProvider.ps1 -Action Restore
```

默认备份文件为 `%LOCALAPPDATA%\OhMyLockScreen\LockAppAumId.clixml`。支持 `-BackupPath` 指定其他备份文件；恢复时使用同一路径。重复启用不会覆盖原始备份，恢复成功后删除已完成的备份，下次启用会重新保存当时的设置。若原值不存在，恢复时仅删除 `LockAppAumId`。备份缺失、损坏或用户不匹配时，恢复会停止。

请在移除应用包前先恢复原设置。

## 验证

脚本检查使用模拟包与内存中的注册表状态，仅在临时目录读写测试备份：

```powershell
powershell -NoProfile -File .\tests\LockScreenProvider.Tests.ps1
```

锁屏激活及宿主重试逻辑的回归检查（链接实际应用代码，使用模拟的 UWP 视图和宿主，不安装包或锁屏）：

```powershell
dotnet run --project .\tests\LockScreenLifecycle\LockScreenLifecycle.csproj
```

该检查覆盖启动专用接口缺失、锁屏两种激活契约、宿主延迟就绪、失败后的手动重试、重试次数上限、取消登录后的重新激活、多视图隔离、页面卸载清理、视图状态显示、Exit 退出和公共 API 空返回后的 broker 回退。托管回退策略使用实际应用代码，原生 broker 调用使用替身，不验证 Windows 实际的宿主授权或内部接口 ABI；这部分仍需要以下实机验收。

手动部署、启用后验收：

1. 按 **Win+L**，确认出现 **It Works!** 和 **Unlock**。
2. 点击 **Unlock**，确认进入系统解锁流程并可按账户要求完成认证。
3. 取消系统凭据界面、返回锁屏后，再次点击 **Unlock**，确认可以重试。
4. 连续锁屏和解锁至少两次，同时覆盖应用未预先运行和已在桌面打开的情况。
5. 执行 `Restore`，再次锁屏，确认恢复原来的锁屏。
6. 测试 Exit 时，分别在桌面预览和系统锁屏中点击按钮，观察应用退出及 Windows 后续行为；若宿主仍为空，保留页面下方四项视图状态及完整错误信息。

若未显示或解锁失败，保留页面上的 HRESULT；可同时查看事件查看器的 **Microsoft-Windows-AppModel-Runtime/Admin** 和 **Microsoft-Windows-TWinUI/Operational** 日志。此实现只包含页面激活和系统解锁请求。
