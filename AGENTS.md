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
- 设置通过 SettingsService 管理，使用 ConfigureFileHelper 读写应用 LocalFolder 下的 Settings.json。设置属性使用 CommunityToolkit.Mvvm 的变更通知机制触发自动保存。
- 当计划使用 OnPropertyChanged/OnPropertyChanging 实现属性变化监听的实现比较繁复复杂，可能会产生较多模板代码时，考虑用 ReactiveUI 和 DynamicData 的方法简化代码。
- 构建需要 .NET 10 SDK、UWP/XAML 构建工具和对应 Windows SDK，沿用项目现有签名配置。
- 锁屏选择通过 windows.lockScreen 扩展和当前用户的 LockAppAumId 配置实现。启用前备份原设置，移除应用包前恢复原设置。
- Figma 设计稿中呈现的 Fluent System Icon 等字体图标的版本可能与项目当前安装的不符，此时应该按字体对应位点的名称语义匹配，而不是位点本身或外观。
- `BrushEditControl.Brush` 使用显式 TwoWay 绑定；编辑整体替换画刷以触发保存。可选 `TargetElement` 仅指定渐变装饰层的坐标区域，显示画刷仍由调用方绑定。
- 笔刷 Button 手柄需显式启用 IsTabStop；拖动通过 AddHandler 的 handledEventsToo 监听已处理的指针事件。
- 渐变拖拽写回时，仅色标位置按百分比保留一位小数（底层 Offset 精度为 0.001）；坐标和半径保留原始计算精度。
- 背景 feature 与渐变装饰层共用目标解析：IBorderable 使用其当前 Border，其他 Control 使用自身；装饰层仅关联编辑视图中的实例。
- UWP 前景依赖属性不是附加属性；前景 feature 按目标类型绑定 Control、TextBlock、RichTextBlock 或 ContentPresenter 自身的 ForegroundProperty，其他 FrameworkElement 附加时无操作，不递归修改子元素，卸载时恢复原值。
- 特性设置控件统一继承 FeatureSettingsControlBase（强类型设置使用其泛型版本）；编辑器通过 TargetElement 统一传递并清理编辑目标，沿用上述目标解析规则。
- ComponentPresenter 通过默认 ContentTemplate 提供装饰 Border，并通过 IBorderable 通知其加载、替换与卸载；背景特性直接修饰 Border，不依赖 UserControl.Background 自动绘制或模板的隐式 DataContext。
- `XamlCompositionBrushBase` 不支持设置继承的变换属性；其默认单位变换在序列化时规范化为 null，读取时兼容旧的单位矩阵。

## 验证原则

- 自动检查使用模拟宿主与独立临时目录，避免修改真实锁屏配置。
- 构建或模拟测试通过不代表 Windows 锁屏集成通过验收；宿主授权、系统解锁及系统兼容性需要实机验证。
