# 代码来源

`src/AppProxyBridge.Core/Storage.cs`、`Upstream.cs`、`Models.cs` 的相关代码，以及测试模拟器 `tests/AppProxyBridge.Core.Tests/Mock.cs`，参考并改写自本机 App Proxy Bridge 预览版；`BridgeEngine.cs` 来自 GPT Proxy Bridge。上述复用部分按 MIT 许可证保留原版权和许可，见 [LICENSE](LICENSE)。

`src/AppProxyBridge.App/App.xaml` 的样式资源及 `SmoothBorder.cs` 参考并改写自 GPT Proxy Bridge，同样适用 [LICENSE](LICENSE)。

`src/AppProxyBridge.App/Assets/app.ico` 沿用本地 App Proxy Bridge 开发预览版的产品图标。

当前没有迁入 WinDivert、ProxyBridge 原生代码、驱动、运行二进制或用户数据。未来若引入其他依赖，须同时记录其版本、来源、修改和分发许可。

