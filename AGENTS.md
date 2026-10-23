# AGENT.md

## 工作规则

- 不要动 README。
- 项目采用 .NET 10 + 自包含发布，优先按照 .NET 10 的编码规范写入格式化的代码。不要写入压缩代码。
- 遵循项目现有约定，优先复用和组合现有组件，尽可能减少修改面。
- 如果有不能确定的地方，请询问用户。

## 项目事实与约定

- 本项目是 Windows UWP 自定义锁屏应用，负责锁屏页面与系统解锁请求；账户凭据验证由 Windows 完成。
- 应用使用 Microsoft.Extensions.Hosting 和 UWP 生命周期。服务统一在 App.Services.cs 中注册，业务服务优先使用构造函数注入；重复激活和多视图共用应用主机。
- 设置通过 SettingsService 管理，使用 ConfigureFileHelper 读写应用 LocalFolder 下的 Settings.json。设置属性使用 CommunityToolkit.Mvvm 的变更通知机制触发自动保存。
- 构建需要 .NET 10 SDK、UWP/XAML 构建工具和对应 Windows SDK，沿用项目现有签名配置。
- 锁屏选择通过 windows.lockScreen 扩展和当前用户的 LockAppAumId 配置实现。启用前备份原设置，移除应用包前恢复原设置。

## 验证原则

- 自动检查使用模拟宿主与独立临时目录，避免修改真实锁屏配置。
- 构建或模拟测试通过不代表 Windows 锁屏集成通过验收；宿主授权、系统解锁及系统兼容性需要实机验证。
