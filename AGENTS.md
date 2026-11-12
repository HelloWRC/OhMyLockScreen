# AGENT.md

## 工作规则

- 不要动 README。
- 项目采用 .NET 10 + 自包含发布，优先按照 .NET 10 的编码规范写入格式化、人类可读的代码。不要写入压缩代码。
- 当且仅当代码的含义不能直接从代码看出，或代码的实现有特殊用意时，添加简短的注释。
- 遵循项目和框架现有约定，优先复用和组合现有组件，尽可能不要重复造轮子或重新实现可通过组合和复用实现的解决方案，尽可能减少修改面。
- 如果有不能确定的地方，请询问用户。
- 当被要求通过 Figma 设计稿实现界面时，优先复用控件库和应用中已有可复用的组件。控件属性若使用了主题值，也应该使用对应控件库提供的主题资源，而不是硬编码。
- 当项目事实与约定更新时，请同步更新 AGENTS.md 中的内容。AGENTS.md 中应包含且仅包含长期有效的事实与规则，且应保持精简。可通过项目文件直接推导出来的内容不用记录。

## 项目事实与约定

- 本项目是 Windows UWP 自定义锁屏应用，负责锁屏页面与系统解锁请求；账户凭据验证由 Windows 完成。
- 应用使用 Microsoft.Extensions.Hosting 和 UWP 生命周期。服务统一在 App.Services.cs 中注册，业务服务优先使用构造函数注入；重复激活和多视图共用应用主机。
- 激活时先捕获目标窗口的 Dispatcher；异步初始化后显式返回该窗口的 UI 线程显示页面，不依赖 SynchronizationContext，窗口关闭或应用退出后跳过显示。
- 应用设置页通过 SettingsPageInfoAttribute 与 AddSettingsPage<T> 注册，由 UWP Frame 创建具有公开无参构造函数的 SettingsPageBase 子类；系统标题栏返回优先处理设置内容 Frame，再返回根 Frame。
- 桌面激活显式关闭 ExtendViewIntoTitleBar，以覆盖 UWP 跨启动保存的标题栏扩展状态。
- 应用在 App 构造时订阅 Application.UnhandledException，将未处理异常标记为已处理并复用当前窗口的 Toast 宿主显示不可自动关闭的错误；Toast 宿主尚未建立时，在窗口内容就绪后补显示。
- 锁屏退出仅由页面收到的未处理 PointerPressed/KeyDown 冒泡事件触发；按键等待路由和应用快捷键处理完毕后再兜底，不使用 CoreWindow 或全局加速键抢占组件输入。Tab 和修饰键保留给焦点导航与组合键；卸载后丢弃排队的输入。
- 锁屏退出动画结束后默认通过 LockScreenHelper 向当前会话的 `Local\LockAppHostGlobalUnlockEvent` 发送事件以请求系统解锁，随后仍退出 UWP 进程，确保下次锁定重新创建锁屏视图；事件不可用或处理失败时回退退出应用。通用设置中的“兼容解锁模式”不发送事件，强制使用旧版退出应用方式。
- 事件解锁后 UWP 进程会保留已退场的 MainPage；锁屏再次激活时必须替换该页面，不能复用已开始退场的视图。
- 设置通过 SettingsService 统一管理，并通过 MVVM 属性变更通知自动保存。
- 场景资产按 SHA-256 Blob 存储，场景配置只保存 Blob ID，并在运行时按当前场景解析 URI；复制整个场景目录即可迁移资产，无独立索引或自动清理。
- 当计划使用 OnPropertyChanged/OnPropertyChanging 实现属性变化监听比较繁复、会产生较多模板代码时，考虑用 ReactiveUI 和 DynamicData 简化。
- 构建需要 UWP/XAML 构建工具、对应 Windows SDK 和 VC++ 14 UWP 运行库 SDK，沿用项目现有签名配置。
- 开发锁屏实际运行已注册包的 AppX 布局；命令行 Build 不会同步该布局，实机复测前需部署或按构建 recipe 同步，并核对运行目录中的程序集版本。
- 动态背景固定使用 GPL 维护版 LibVLC.UWP；不得换回旧 VideoLAN.LibVLC.UWP，其内含的 VLC 3.0.16 存在 WinRT D3D11 设备重复 AddRef 泄漏。
- 视频呈现使用自有 BackgroundVideoView/VideoSwapChain 管理生命周期，避免 LibVLCSharp VideoView 的全局匿名事件引用；释放时先停止并销毁播放器，再解绑面板与交换链，执行 D3D ClearState/Flush 后释放上下文和设备。
- 视频交换链的宽高私有数据 GUID 必须匹配 VLC WinRT 输出协议；原生检查使用独立协议常量，实际播放需在隔离 UWP 预览包中验证解码帧与输出像素。
- VLC 3 不将负数 input-repeat 视为无限循环；背景通过 EndReached 调度到所属 UI 线程后 Stop/Play 重播，并在有效视频输出建立后重新应用裁剪比例。LibVLCSharp 回调的 sender 是内部事件管理器，订阅时需捕获对应播放器，不能用 sender 识别播放器。
- 锁屏选择通过 windows.lockScreen 扩展和当前用户的 LockAppAumId 配置实现。运行时由独立 NativeAOT full-trust Helper 直接读写该值；关闭替换前通过 ContentDialog 提示资源管理器将重启，用户确认后才向 32 位和 64 位注册表视图写入 Microsoft.LockApp_cw5n1h2txyewy!WindowsDefaultLockScreen，并重启当前会话中缓存锁屏扩展的 LockAppHost/LockApp 与桌面 Explorer，让 Windows 丢弃锁屏扩展缓存并回退到默认锁屏，不保留原值备份。
- 同一 Application 只能声明一个 windows.fullTrustProcess；独立锁屏 Helper 使用单独 Application Id 和参数组启动。
- 包清单必须通过 desktop6:RegistryWriteVirtualization 禁用 MSIX 注册表写入虚拟化；否则 Helper 的 HKCU 写入不会落到用户真实注册表。
- Figma 设计稿中呈现的 Fluent System Icon 等字体图标版本可能与项目当前安装的不符，此时按字体位点名称的语义匹配，而不是位点本身或外观。
- 特性按目标类型应用原生属性，不递归修改子元素；卸载时恢复目标原绑定或本地值。组件默认特性仅在新建配置时生成，加载或重建视图时不补齐。
- WinUI 2 TreeView 不处理集合的 Move 通知；组件树排序使用移除再插入并保留节点实例，协调期间忽略临时选择回调，完成后重新同步选中项。
- 拖入画布的 Grid/Canvas 先按左上角落点定位；同批图片固定父容器，流式布局仅调整插入顺序。组件树行的上下边缘用于同级插入，仅中间区域允许放入容器。
- 媒体通过共享 IMediaStateService 读取系统当前 SMTC 会话；共享快照仅包含数据，UI 图像和画刷由各视图创建。异步结果显式返回所属 Dispatcher，卸载后忽略过期结果。
- IsEditing 从场景显式传递到嵌套组件且不持久化；编辑时 Presenter 不同步组件 Visibility，无媒体会话使用隔离占位数据，完整预览恢复真实行为；媒体默认仅在无会话时隐藏。
- 系统状态通过共享 ISystemStatusService 按已加载实例的状态类型启停监听，快照仅包含数据；视图更新回到所属 Dispatcher，卸载后丢弃旧结果。
- 音频频谱通过共享 IAudioSpectrumService 按租约连接桌面 AudioHost（需 runFullTrust），避免锁屏 UWP 宿主 loopback 静音；辅助进程复用包内自包含运行时，通过当前会话的包内管道传递频谱快照，无客户端后释放采集并自动退出。

## 验证原则

- 自动检查使用模拟宿主与独立临时目录，避免修改真实锁屏配置。
- 构建或模拟测试通过不代表 Windows 锁屏集成通过验收；宿主授权、系统解锁及系统兼容性需要实机验证。
