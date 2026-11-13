# OhMyLockScreen

> [!caution]
> 本应用还在早期实验阶段，可能会出现不稳定的情况，请谨慎在主力设备上使用。

![截图](image/README/PixPin_2026-11-13_15-33-47.png)

> 你的锁屏，如你所想

OhMyLockScreen 是一款适用于 Windows 的锁屏自定义工具，可以实现在不替换系统文件和注入系统进程的前提下干净地自定义系统锁屏界面。本应用基于 .NET 10 + UWP 构建。

OhMyLockScreen 不能——且以后也不会——替换或修改锁屏后的登录界面。

## 功能

- [x] 直观的编辑界面
- [x] 自定义锁屏页面为你喜欢的样子
- [x] 设置动态锁屏壁纸
- [x] 通过组件系统自由组合锁屏界面的元素
- [x] 通过特性系统为界面元素应用各种各样的样式
- [ ] 更多功能正在开发中……

## 开始使用

> 等微软商店审核完后补充

## 开发

### 开发环境

- 在 Windows 上安装 Git 和 .NET 10 SDK，并开启开发人员模式。
- 安装 Visual Studio 2026，选择「WinUI 应用程序开发」工作负载，并勾选以下组件。安装详情请参阅[微软 UWP 开发环境文档](https://learn.microsoft.com/zh-cn/windows/uwp/dotnet-native/modernize-uwp-apps-with-dotnet)。
  - UWP 工具
  - Windows 11 SDK（`10.0.26100.0`）
  - C++ UWP 工具（含 VC++ v14 UWP 运行库及 x86/x64 MSVC 工具）

### 运行项目

1. 克隆仓库后，用 Visual Studio 打开 `OhMyLockScreen.slnx`，将 `OhMyLockScreen` 设为启动项目。
2. 选择 `Debug | x64`（也支持 `x86`），按 `F5` 部署并运行。
3. 如果部署提示缺少签名证书，请在主项目的打包设置中创建并选择本地测试证书，证书主题须与 `Package.appxmanifest` 中的 `Publisher` 一致。
4. 如需测试系统锁屏，请在应用的【设置】→【通用】中开启「替换系统锁屏实现」，再按 `Win + L`。修改代码后，请重新部署再测试。

## 许可证

本项目基于 [GPLv3](./LICENSE) 获得许可。
