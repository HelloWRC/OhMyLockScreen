# AGENT.md

## 工作规则

- 除非用户明确要求，不要动 README。
- 项目采用 .NET 10 + 自包含发布，优先按照 .NET 10 的编码规范写入格式化、人类可读的代码。不要写入压缩代码。
- 界面优先使用 XAML 实现。XAML 可实现的情况下禁止通过 C# 代码声明界面。
- 当且仅当代码的含义不能直接从代码看出，或代码的实现有特殊用意时，添加简短的注释。
- 遵循项目和框架现有约定，优先复用和组合现有组件，尽可能不要重复造轮子或重新实现可通过组合和复用实现的解决方案，尽可能减少修改面。
- 被要求修复缺陷时，请尽可能在能干净实现的情况下修复其根因，而不是做表面修复。
- 如果有不能确定的地方，请询问用户。
- 界面统一使用 XAML 声明；C# 只负责数据、交互逻辑和运行时计算，不在代码中拼装控件树。
- 当被要求通过 Figma 设计稿实现界面时，优先复用控件库和应用中已有可复用的组件。控件属性若使用了主题值，也应该使用对应控件库提供的主题资源，而不是硬编码。
- 当项目事实与约定更新时，请同步更新 AGENTS.md 中的内容。AGENTS.md 中应包含且仅包含长期有效的事实与规则，且应保持精简。可通过项目文件直接推导出来的内容不用记录。
- 每轮对话完成前尽最大努力验证用户要求的工作是否全部实现。若检出缺陷，请修复。未验证的地方请如实告知用户。
- 严格遵守用户的要求，禁止随意发挥，禁止扩大改动范围，禁止偷工减料。要扩大或舍弃某些设计和实现时，预先和用户沟通确认，说明为什么要这么做，代价和收益有什么，禁止擅自行动。

## 项目事实与约定

- 本项目是 Windows UWP 自定义锁屏应用，负责锁屏页面与系统解锁请求；账户凭据验证由 Windows 完成。
- 应用使用 Microsoft.Extensions.Hosting 和 UWP 生命周期。服务统一在 App.Services.cs 中注册，业务服务优先使用构造函数注入；重复激活和多视图共用应用主机。
- 主宿主的各视图共享同一份本地磁盘日志；归档仅管理 `LocalState/Logs` 中本写入器的日志，不触碰 NativeHost 的专用诊断文件。
- 激活时先捕获目标窗口的 Dispatcher；异步初始化后显式返回该窗口的 UI 线程显示页面，不依赖 SynchronizationContext，窗口关闭或应用退出后跳过显示。
- 主包以 `App` 同时承接开始菜单和锁屏激活。按激活类型和当前窗口宿主识别锁屏视图；桌面启动进入场景管理器（`SceneManagerPage`），并通过根 `Frame` 导航至场景编辑器、全屏预览页（`ScenePreviewPage`）与应用设置页。
- 应用设置页通过 SettingsPageInfoAttribute 与 AddSettingsPage<T> 注册，由 UWP Frame 创建具有公开无参构造函数的 SettingsPageBase 子类；系统标题栏返回优先处理设置内容 Frame，再返回根 Frame。
- 桌面激活显式关闭 ExtendViewIntoTitleBar，以覆盖 UWP 跨启动保存的标题栏扩展状态。
- 应用在 App 构造时订阅 Application.UnhandledException，以 Critical 记录原异常和堆栈（主宿主尚未建立时在建立后补记），将异常标记为已处理并复用当前窗口的 Toast 宿主显示不可自动关闭的错误；Toast 宿主尚未建立时，在窗口内容就绪后补显示。
- 锁屏与全屏预览页（`ScenePreviewPage`）共享 `LockScreenDismissalController` 与 `ScenePresenter`：指针手势从页面收到的未处理 PointerPressed 开始，轻触和上滑均在抬起时判定，取消或失去捕获只回弹；按键等待路由和应用快捷键处理完毕后再兜底，不使用 CoreWindow 或全局加速键抢占组件输入，Tab 和修饰键保留给焦点导航与组合键，卸载后丢弃排队的输入。预览页进入后全屏，退场或返回仅退出全屏并返回上一页，不触发系统解锁或退出进程。
- 锁屏退出动画期间预启动一次性 NativeHost 并连接包内命名管道，预定在动画结束前 80 ms 提交一次解锁指令。NativeHost 向当前会话的 `Local\LockAppHostGlobalUnlockEvent` 发送事件，UWP 等待动画和解锁请求完成后退出，页面提前卸载时沿用一次性解锁和退出流程。管道准备失败时回退文件请求；指令已开始发送后避免重复触发事件。兼容解锁模式直接走退出应用方式。
- 事件解锁后 UWP 进程会保留已退场的 MainPage；锁屏再次激活时必须替换该页面，不能复用已开始退场的视图。
- 解锁路径用跨进程性能计数器测量主应用提交请求至 NativeHost 解析请求的耗时，并在发送系统事件后将传输方式和耗时追加到 `LocalState/LockScreenProvider/unlock-latency.log`。
- 设置通过 SettingsService 统一管理，并通过 MVVM 属性变更通知自动保存。
- 场景资产按 SHA-256 Blob 存储，场景配置只保存 Blob ID（含预览图 `PreviewImageBlobId`），并在运行时按当前场景解析 URI；复制整个场景目录即可迁移资产，无独立索引或自动清理。`.omls` 场景包为 ZIP 压缩包：导出原样打包 `scene.json` 与目录内全部 Blob 文件不做裁剪（跳过临时/备份文件并拒绝链接）；导入分配新 `SceneId`、校验路径穿越与 Blob SHA-256 后原子提交。应用通过 `windows.fileTypeAssociation` 声明 `.omls` 文件关联与 `ScenePackageLogo` 图标集；双击文件激活（`OnFileActivated`）将桌面视图切换至 `SceneManagerPage`（切离编辑器前先落盘待保存变更），与场景管理页拖拽导入共用按窗口 `ScenePackageImportQueue`、`ScenePackageImport` 轻量元数据预览及确认弹窗串行导入流程。
- 静态壁纸模式未指定图片资产时，由场景呈现器在加载及窗口重新激活时读取当前系统锁屏壁纸；静态壁纸和视频共用背景视口的退场和视差动画。
- 场景编辑项与启用的锁屏场景独立选择；已启用场景可以重命名或复制，删除前必须先启用另一个场景。场景管理器顶部导航条复用 UWP 原生 `Pivot`。
- 当计划使用 OnPropertyChanged/OnPropertyChanging 实现属性变化监听比较繁复、会产生较多模板代码时，考虑用 ReactiveUI 和 DynamicData 简化。
- 构建需要 UWP/XAML 构建工具、对应 Windows SDK 和 VC++ 14 UWP 运行库 SDK，沿用项目现有签名配置。
- 正式项目默认从当前 HEAD 可达的最近本地 Git 标签设置程序集版本，主项目同时设置 MSIX 版本。标签必须是四段无前导零的十进制数字，每段为 0 至 65534，无可达标签时使用 `0.0.0.0`。程序集信息版本为 `版本+完整提交哈希`；关于页和启动日志通过公开 API 使用 `版本-代号-七位提交哈希(分支)`。
- 开发锁屏实际运行已注册包的 AppX 布局；命令行 Build 不会同步该布局，实机复测前需部署或按构建 recipe 同步，并核对运行目录中的程序集版本。
- 动态背景固定使用 GPL 维护版 LibVLC.UWP；不得换回旧 VideoLAN.LibVLC.UWP，其内含的 VLC 3.0.16 存在 WinRT D3D11 设备重复 AddRef 泄漏。
- 视频呈现使用自有 BackgroundVideoView/VideoSwapChain 管理生命周期，避免 LibVLCSharp VideoView 的全局匿名事件引用；释放时先停止并销毁播放器，再解绑面板与交换链，执行 D3D ClearState/Flush 后释放上下文和设备。
- 视频交换链的宽高私有数据 GUID 必须匹配 VLC WinRT 输出协议；原生检查使用独立协议常量，实际播放需在隔离 UWP 预览包中验证解码帧与输出像素。
- VLC 3 不将负数 input-repeat 视为无限循环；背景通过 EndReached 调度到所属 UI 线程后 Stop/Play 重播，并在有效视频输出建立后重新应用裁剪比例。LibVLCSharp 回调的 sender 是内部事件管理器，订阅时需捕获对应播放器，不能用 sender 识别播放器。
- 应用通过 `windows.lockScreen` 声明锁屏扩展，由 NativeHost 只读查询当前用户注册表的 `LockAppAumId` 以检测启用状态。用户按首页链接中的指南在应用外切换锁屏。
- NativeHost 由同一 full-trust 扩展的参数组启动，音频采集、一次性锁屏命令和桌面编辑器恢复各自管理进程生命周期。
- 桌面场景编辑器激活后在所属 UI 线程检查 CoreWindow 的根窗口类；超时后请求 NativeHost 独立显示恢复提示，本体随即退出。用户确认后，NativeHost 先终止仍存活的请求进程，再重启当前会话的 Explorer，通过进程外系统激活启动应用，并等待可见编辑器窗口确认就绪。
- Figma 设计稿中呈现的 Fluent System Icon 等字体图标版本可能与项目当前安装的不符，此时按字体位点名称的语义匹配，而不是位点本身或外观。
- 特性按目标类型应用原生属性，不递归修改子元素；卸载时恢复目标原绑定或本地值。`FeatureInfo.IsEnabled` 默认为 `true`，禁用项由 `FeatureAttacher` 跳过挂载，并在编辑器中保持半透明且可交互。组件默认特性仅在新建配置时生成，加载或重建视图时不补齐。
- 网页组件使用 WinUI 2 WebView2 呈现 HTTP(S) 页面。网页和外层 Presenter 默认禁用输入与命中，可按组件启用交互。组件在编辑器中通过组件树访问设置，在完整预览与锁屏中接收已启用的网页输入。
- 组件树由编辑器根据指针手势和落点提交模型移动。WinUI 2 TreeView 不处理集合的 Move 通知，树节点集合通过移除再插入同步顺序并保留节点实例，协调期间忽略临时选择回调，完成后重新同步选中项。
- 拖入画布的 Grid/Canvas 先按左上角落点定位；同父拖拽保留 Canvas/Grid 的层级顺序，流式布局仅调整插入顺序。同批图片固定父容器；组件树容器行的边缘和叶节点行的上下半区用于同级插入，容器行中间用于放入容器。
- 媒体通过共享 IMediaStateService 读取系统当前 SMTC 会话；共享快照仅包含数据，UI 图像和画刷由各视图创建。异步结果显式返回所属 Dispatcher，卸载后忽略过期结果。
- IsEditing 从场景显式传递到嵌套组件且不持久化；编辑画布用斜条纹透明遮罩标识本该隐藏的组件，无媒体会话使用隔离占位数据但按真实状态判断隐藏，完整预览恢复真实行为；媒体默认仅在无会话时隐藏。
- 系统状态通过共享 ISystemStatusService 按已加载实例的状态类型启停监听，快照仅包含数据；视图更新回到所属 Dispatcher，卸载后丢弃旧结果。
- 天气沿用 ClassIsland 的共享 IWeatherService、五分钟 DispatcherTimer、`Settings.LastWeatherInfo` 和按 ID 选择的图标模板注册表。注册表只保存资源键，模板与场景图标字体在使用它们的视图线程上解析，不跨视图共享 XAML 对象。锁屏使用已保存的位置，Windows 定位权限只在桌面前台请求。
- 设置页通过 `SettingsPageBase.OpenDrawer` 和 `CloseDrawer` 使用 Shared 中的 DrawerHost。每个设置窗口有独立的抽屉，返回优先关闭抽屉。
- 音频频谱通过共享 IAudioSpectrumService 按租约连接 NativeHost，避免锁屏 UWP 宿主 loopback 静音；通过当前会话的包内管道传递频谱快照，无客户端后释放采集并自动退出。
- 规则类型由宿主和插件注册；规则集由调用方持有。规则编辑器可独立托管，运行时判定状态不序列化、不触发场景保存。「按规则隐藏」在编辑时也求值，与组件自身的隐藏意图合并，不改变组件内部显示项。
- `ScenePresenter`、`LayerPresenter` 与 `ComponentPresenter` 均实现 `IDecoratorHost`，由通用单子节点链式装饰器 `ChainedDecorator`/`DecoratorChain` 按优先级升序（值越小越靠外）动态组合外部包装层；链更新保留外层未变前缀，可位于其子树内的装饰器、呈现器与组件在 `Unloaded` 中以 `IsLoaded` 防护 UWP 同帧重挂投递的过期卸载事件。`AcquireShared` 按键引用计数复用同一装饰节点（如 `ComponentPresenter` 上的 `BackgroundFeature` 与 `BorderFeature` 共享 `DecoratorKeys.Border`），编辑器隐藏条纹遮罩（`StripeMaskDecorator`）通过实现 `IVisualRedirectSourceProvider` 配合外层 `ShadowDecorator` 避免嵌套 `RedirectVisual` 丢失。
- 图层视差特性（`omls.features.layout.parallax`）通过 `ISceneParallaxHost` 共享 `ScenePresenter` 的视差驱动与计时器，直接观察 `LayerPresenter.ParallaxHost` 并在 `LayerPresenter.Decorators` 注册带 `CompositeTransform` 的视差包装层（优先级 `DecoratorPriorities.Parallax`），与 `TransformFeature` 独立叠加且不受 `SuppressParallaxWithoutBackground` 抑制。
- 阴影特性（`omls.features.appearance.shadow`）面向 `IDecoratorHost`（覆盖场景、图层与组件），在目标 `Decorators` 注册 `ShadowDecorator`（优先级 `DecoratorPriorities.Shadow`）并绑定 `ShadowSpec`，在内容后方通过 `LayerVisual`、`RedirectVisual` 与 `AlphaMaskEffect` + `GaussianBlurEffect` 按内容轮廓投影，与 `TransformFeature`、视差包装层及编辑器隐藏条纹遮罩独立共存。
- 描边特性（`omls.features.appearance.stroke`）面向 `IDecoratorHost`（覆盖场景、图层与组件），在目标 `Decorators` 注册 `StrokeDecorator`（优先级 `DecoratorPriorities.Stroke`，位于编辑器遮罩之内、共享 `Border` 之外）并绑定 `StrokeSpec`（画刷、粗细、镂空与内部/中心/外部位置），使用纯 Composition 线性（`K=1`）迭代高斯阈值形态学（大半径内部降半分辨率并全分辨率抛光）、纯色 `CompositionColorBrush` 快速路径、分桶边距与按 `Compositor` 缓存的 `CompositionEffectFactory`（亚克力分支走 `LayerVisual` + `RedirectVisual`），并通过 `IVisualOutsetProvider` 与 `StripeMaskDecorator`、`ShadowDecorator` 同步外扩边距。
- 不透明度特性（`omls.features.appearance.opacity`）面向 `IDecoratorHost`（覆盖场景、图层与组件），在目标 `Decorators` 注册 `OpacityDecorator`（优先级 `DecoratorPriorities.Opacity`，位于视差之内、阴影之外）并绑定 `OpacityEffectSpec`（不透明度与 `Normal` 加 21 种 Composition 硬件混合方式）：全不透明正常或全透明走零表面直设快路径；`Normal` 半透明与非正常混合均通过高 DPI `CompositionVisualSurface` + `SpriteVisual` 实现整组编组合成，混合分支使用按 `Compositor` 缓存的 `Blend(Content, Backdrop) - DestinationOut(Backdrop, Content)` 保持前景轮廓 Alpha（显式对调 D2D `BlendEffect` 输入以对齐 Photoshop/W3C 语义），并通过 `IVisualOutsetProvider` 汇总并转发含 `ShadowDecorator` 非对称外扩在内的渲染边距。
- 图层（`LayerSettings`）与组件（`ComponentSettings`）持久化 `IsVisible`（默认 `true`）和 `IsLocked`（默认 `false`），由场景树行右侧透明切换按钮控制：显隐优先级高于规则集（`HideByRulesFeature`）与组件运行时隐藏，隐藏后在编辑器与运行态均完全折叠（不显示斜条纹遮罩）并沿子树继承；锁定后节点仍正常显示，但在编辑器画布内不可悬停、选中、拖拽、缩放或作为画布拖放落点，保留树内选择与属性面板编辑。

## 验证原则

- 自动检查使用模拟宿主与独立临时目录，避免修改真实锁屏配置。
- 构建或模拟测试通过不代表 Windows 锁屏集成通过验收；宿主授权、系统解锁及系统兼容性需要实机验证。
