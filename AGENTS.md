# AGENT.md

## 工作规则

- 不要动 README。
- 项目采用 .NET 10 + 自包含发布，优先按照 .NET 10 的编码规范写入格式化的代码。不要写入压缩代码。
- 遵循项目和框架现有约定，优先复用和组合现有组件，尽可能不要重复造轮子或重新实现可通过组合和复用实现的解决方案，尽可能减少修改面。
- 如果有不能确定的地方，请询问用户。
- 当被要求通过 Figma 设计稿实现界面时，优先复用控件库和应用中已有可复用的组件。
- 当项目事实与约定更新时，请同步更新 AGENTS.md 中的内容。AGENTS.md 中应包含且仅包含长期有效的事实与规则，且应保持精简。

## 项目事实与约定

- 本项目是 Windows UWP 自定义锁屏应用，负责锁屏页面与系统解锁请求；账户凭据验证由 Windows 完成。
- 应用使用 Microsoft.Extensions.Hosting 和 UWP 生命周期。服务统一在 App.Services.cs 中注册，业务服务优先使用构造函数注入；重复激活和多视图共用应用主机。
- 激活时先捕获目标窗口的 Dispatcher；异步初始化后显式返回该窗口的 UI 线程显示页面，不依赖 SynchronizationContext，窗口关闭或应用退出后跳过显示。
- 应用设置页通过 SettingsPageInfoAttribute 与 AddSettingsPage<T> 注册，由 UWP Frame 创建具有公开无参构造函数的 SettingsPageBase 子类。宿主标题默认限宽 960 逻辑像素，可用 `[FullWidthPage]` 解除上限，或用 `[HidePageTitle]` 隐藏。系统标题栏返回优先处理设置内容 Frame，再返回根 Frame。设置页默认清除导航历史且禁用页面缓存。
- 设置宿主位于 `OhMyLockScreen/Views`，设置子页位于 `OhMyLockScreen/Views/SettingsPages`。宿主的设置 Frame 填满可用内容区，正文宽度、边距和滚动由各子页决定。页面基类、元数据和注册逻辑在 Shared 中按类型归入对应目录。
- 设置宿主沿用 WinUI 主题资源和控件默认样式；设置内容 Frame 使用内置 `NavigationThemeTransition` 与 `SlideNavigationTransitionInfo`（`FromBottom`）实现滑入和淡出，不自定义 Storyboard。应用资源字典提供 `SettingsContainerWidth` 和 `SettingsStackPanelStyle`。引用该样式的正文在自适应边距内水平拉伸，达到最大宽度后居中。
- 桌面激活显式关闭 ExtendViewIntoTitleBar，以覆盖 UWP 跨启动保存的标题栏扩展状态。
- 锁屏退出仅由页面收到的未处理 PointerPressed/KeyDown 冒泡事件触发；按键等待路由和应用快捷键处理完毕后再兜底，不使用 CoreWindow 或全局加速键抢占组件输入。Tab 和修饰键保留给焦点导航与组合键；卸载后丢弃排队的输入。
- 设置通过 SettingsService 管理，使用 ConfigureFileHelper 读写应用 LocalFolder 下的 Settings.json。设置属性使用 CommunityToolkit.Mvvm 的变更通知机制触发自动保存。
- 场景资产保存在 `Scenes/<场景 ID>/Blobs/<SHA-256 前两位>/<SHA-256>`，文件名使用小写十六进制且无扩展名。场景配置只保存 Blob ID，运行时用当前场景解析 URI。复制整个场景目录可迁移资产，无独立索引或自动清理。
- 当计划使用 OnPropertyChanged/OnPropertyChanging 实现属性变化监听的实现比较繁复复杂，可能会产生较多模板代码时，考虑用 ReactiveUI 和 DynamicData 的方法简化代码。
- 构建需要 .NET 10 SDK、UWP/XAML 构建工具、对应 Windows SDK 和 VC++ 14 UWP 运行库 SDK，沿用项目现有签名配置。
- 开发锁屏实际运行已注册包的 AppX 布局；命令行 Build 不会同步该布局，实机复测前需部署或按构建 recipe 同步，并核对运行目录中的程序集版本。
- 动态背景使用 LibVLCSharp.UWP 和 LibVLC.UWP 3.0.23-2608082208（GPL 维护构建），各 ScenePresenter 独立管理播放器与交换链，卸载时释放；应用仅构建和发布 x86、x64。旧 VideoLAN.LibVLC.UWP 3.3.2 内的 VLC 3.0.16 存在 WinRT D3D11 设备重复 AddRef 泄漏，不再使用。
- 视频呈现使用自有 BackgroundVideoView/VideoSwapChain 管理生命周期，避免 LibVLCSharp VideoView 的全局匿名事件引用；释放时先停止并销毁播放器，再解绑面板与交换链，执行 D3D ClearState/Flush 后释放上下文和设备，子视图单独卸载也遵循此顺序。
- 视频交换链的宽高私有数据 GUID 必须匹配 VLC WinRT 输出协议；原生检查使用独立协议常量，实际播放检查在隔离 UWP 预览包中验证解码帧与输出像素。
- VLC 3 不将负数 input-repeat 视为无限循环；背景通过 EndReached 调度到所属 UI 线程后 Stop/Play 重播，每次启动前设置裁剪比例，并仅在有效视频输出建立后重新应用。LibVLCSharp 回调的 sender 是内部事件管理器，订阅时需捕获对应播放器，不能用 sender 识别播放器。
- 锁屏选择通过 windows.lockScreen 扩展和当前用户的 LockAppAumId 配置实现。启用前备份原设置，移除应用包前恢复原设置。
- Figma 设计稿中呈现的 Fluent System Icon 等字体图标的版本可能与项目当前安装的不符，此时应该按字体对应位点的名称语义匹配，而不是位点本身或外观。
- `BrushEditControl.Brush` 使用显式 TwoWay 绑定；编辑整体替换画刷以触发保存。可选 `TargetElement` 仅指定渐变装饰层的坐标区域，显示画刷仍由调用方绑定。
- 笔刷 Button 手柄需显式启用 IsTabStop；拖动通过 AddHandler 的 handledEventsToo 监听已处理的指针事件。
- 数字输入框通过共享样式隐藏内部清除按钮，并将其布局宽度归零，避免聚焦编辑时挤占输入空间。
- 渐变拖拽写回时，仅色标位置按百分比保留一位小数（底层 Offset 精度为 0.001）；坐标和半径保留原始计算精度。
- 背景 feature 与渐变装饰层共用目标解析：IBorderable 使用其当前 Border，其他 Control 使用自身；装饰层仅关联编辑视图中的实例。
- 边框 feature 仅附加到 IBorderable 的当前 Border，修改框线、圆角和 Padding；替换或卸载时恢复原值。默认框线复制添加时的主题前景色，作为独立纯色画刷保存。
- UWP 前景依赖属性不是附加属性；前景 feature 按目标类型绑定 Control、TextBlock、RichTextBlock 或 ContentPresenter 自身的 ForegroundProperty，其他 FrameworkElement 附加时无操作，不递归修改子元素，卸载时恢复原值。
- 文本与字体 feature 按目标类型应用原生字体属性并沿用 UWP 继承；组件通过 ITextStyleTarget 公开行高、对齐和装饰线的文本目标，不递归查找子元素。行高使用逻辑像素（0 为自动），字距使用千分之一 em。
- 文本、时钟和日期的字号及字重由默认特性提供，不在组件内部设置；依赖属性绑定卸载时恢复原绑定或本地值，无本地值时 ClearValue，以保留原生继承。
- 特性设置控件统一继承 FeatureSettingsControlBase（强类型设置使用其泛型版本）；编辑器通过 TargetElement 统一传递并清理编辑目标，沿用上述目标解析规则。
- 布局设置控件的 TargetElement 使用布局特性所在的 Presenter；仅在编辑控件加载期间，将自动方向的实际宽高按逻辑像素取整回写设置，初次加载与目标变更时同步，不改变自动开关。
- ComponentPresenter 通过默认 ContentTemplate 提供装饰 Border，并通过 IBorderable 通知其加载、替换与卸载；背景特性直接修饰 Border，不依赖 UserControl.Background 自动绘制或模板的隐式 DataContext。
- ComponentPresenter 内容层按有效前景同步画刷；亚克力和径向渐变使用独立实例，随内容重建或卸载更新，避免子项重排断开共享合成画刷。
- 组件可通过 ComponentDefaultFeaturesAttribute 指定具有公开无参构造函数的 IComponentDefaultFeaturesProvider；ComponentRegisterInfo.CreateSettings 仅在新建配置时生成独立的默认特性及设置，加载或重建视图时不补齐。编辑器仅在未声明布局特性时添加居中布局。
- `XamlCompositionBrushBase` 不支持设置继承的变换属性；其默认单位变换在序列化时规范化为 null，读取时兼容旧的单位矩阵。
- 编辑器面板动画由页面统一管理可见性与时间进度；入场与出场均使用 cubic-bezier(0,0,0,1)，组件侧栏与画布留白同步变化，不再直接绑定面板 Visibility 或画布 Margin 到最终页签状态。
- 编辑器通过 ScenePresenter.BackgroundViewportSize 固定视频渲染尺寸，并在场景视口内居中裁剪；侧栏动画的左右留白对齐物理像素，避免视频交换链反复缩放和居中取整产生抖动。
- 编辑器通过 LockScreen.GetImageStream 读取系统锁屏图像作为 ScenePresenter 的最底层预览背景，加载与窗口重新激活时刷新，卸载时清理；读取需要 picturesLibrary 能力，真实锁屏不启用此底图。
- 编辑器在画布与组件树接收文件拖放；图片按解码像素宽高创建固定逻辑尺寸的布局容器，背景画刷引用场景 Blob。同批图片固定父容器，仅首个视频作为场景背景；导入提交需检查场景、页面和预览状态是否仍有效。
- 组件库、组件树及图片拖入画布的 Grid/Canvas 时，先通过布局特性将左上角定位到落点；Grid 首次布局后按实际尺寸复用移动时的自动对齐规则并换算 Margin，保留落点、行列、跨度及尺寸开关。同批图片复用导入前记录的父容器内落点，流式布局仅调整插入顺序。
- 布局对齐选项仅在编辑控件加载后接收选择写回；初始化及从设置刷新选项期间不写回，避免缓存选择覆盖拖放位置。
- WinUI 2 TreeView 不处理集合的 Move 通知；组件树排序使用移除再插入并保留节点实例，协调期间忽略临时选择回调，完成后重新同步选中项。
- 组件树键盘排序在 PreviewKeyDown 中通过场景模型提交；延迟拖放需验证场景及源、目标模型仍有效，跨容器移动按模型保留子树展开状态与选择。
- 组件树行的上下边缘用于同级插入，仅中间区域允许放入容器。
- 媒体通过共享 IMediaStateService 读取系统当前 SMTC 会话，需声明 uap7:globalMediaControl；共享快照仅包含数据，UI 图像和画刷由各视图创建。封面主题色在后台提取原始值，由控件暗化。
- 媒体控件的异步加载与命令完成不依赖 SynchronizationContext；等待前捕获数据和 CanvasDevice，资源提交、依赖属性访问及重绘显式返回所属 Dispatcher。卸载后忽略过期的加载与交互结果。
- IsEditing 从场景显式传递到嵌套组件，不持久化；编辑时 Presenter 不同步组件 Visibility，无媒体会话使用隔离占位数据；全屏预览恢复真实行为。媒体默认仅在无会话时隐藏，暂停仍显示。
- 组合媒体复用文本、单功能控制按钮、封面及进度条控件；四个独立部件不预加装饰特性，封面保留自适应主题色边缘效果。
- 两种沉浸媒体形态的封面随组件实际高度铺满；140/200 仅作为未约束高度时的默认测量尺寸，不固定内部容器高度。
- 系统状态通过共享 ISystemStatusService 按已加载实例的状态类型启停监听，快照仅包含数据；视图更新回到所属 Dispatcher，卸载后丢弃旧结果。蓝牙仅统计已连接设备并按容器或地址去重；音量通过 ActivateAudioInterfaceAsync 读取默认输出端点，源生成 COM 回调须实现 IAgileObject，通知中不阻塞或释放端点。
- 系统状态组件的 IconSize 独立控制图标大小，默认 24 逻辑像素；右侧文字沿用文本特性与原生字号继承，新建组件默认字号为 16，二者互不影响。
- 网络状态按连接配置和 IANA 接口类型区分 Wi-Fi、有线与移动数据；默认连接为 VPN/TUN 等未知类型时，优先选取连通性最高的已连接、可识别网络，并保持名称与类型来自同一配置。断网使用通用无网络图标。
- 音频频谱通过共享 IAudioSpectrumService 按租约连接桌面 AudioHost（需 runFullTrust），避免锁屏 UWP 宿主的 loopback 静音；辅助进程复用包内自包含运行时，在 MTA 线程采集默认输出并执行 FFT，仅通过当前会话的包内管道传递频谱快照，无客户端后释放采集并自动退出。各视图独立平滑、裁剪完整前景画刷，卸载或窗口关闭时释放租约及资源；静态示例仅用于静音时的编辑状态。

## 验证原则

- 自动检查使用模拟宿主与独立临时目录，避免修改真实锁屏配置。
- 构建或模拟测试通过不代表 Windows 锁屏集成通过验收；宿主授权、系统解锁及系统兼容性需要实机验证。
