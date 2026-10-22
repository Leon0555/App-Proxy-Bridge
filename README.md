# App Proxy Bridge

<p align="center"><img src="AppProxyBridge.App/Assets/logo.png" width="160" alt="App Proxy Bridge logo: multiple nodes converge at one hub and fan out again" /></p>

Windows 按软件代理管理器，沿用 GPT Proxy Bridge 的 WPF 界面、主题、配置管理和 DPAPI 凭据存储。两个项目及其个人数据互相独立。

**当前为 0.1.0 开发预览，不能视为已完成实际流量接管验收。** 本机安全软件阻止官方 WinDivert SDK 压缩包解压，因此没有安装驱动、安装服务或让真实软件经过此工具。开发预览包缺少 WinDivert 运行组件时，会拒绝启用代理。

## 使用方式

完整安装包安装后台服务后，普通权限运行界面。添加 EXE、正在运行的软件或开始菜单应用，在软件列表勾选需要代理的条目。辅助 EXE 单独添加到同一条目；不会自动代理全部子进程。

在“代理配置”保存 HTTP、HTTPS 或 SOCKS5 配置，再点击“应用更改 / 启用代理”。多个软件共用一个生效配置。保存与应用分开，切换代理配置不会改变软件勾选。

已建立的连接保留原路径；首次接入时，让软件重新连接。代理切换后旧代理连接保留其原网关，新连接使用新配置。HTTP/HTTPS 或不支持 UDP 的 SOCKS5 上游会阻止已选软件的 UDP，不回退直连。

关闭窗口后保持托盘运行；停止或退出会关闭代理连接并释放接管。服务开机启动时待机，手动启用代理。整个引擎退出后恢复系统联网，不提供持续防直连机制。

不修改系统代理、系统 DNS 或默认路由。安装时仅为本工具的转发端口添加指定防火墙规则。共享系统 DNS、共享后台服务、未添加组件和不能确定归属的流量不保证经代理。

## 数据

个人数据：`%LOCALAPPDATA%\AppProxyBridge`。配置格式版本 2；软件列表全局保存，代理密码使用当前用户 DPAPI 加密。服务通信版本 1，通过本机命名管道进行，限制为安装用户；不接受远程连接。

“导入旧项目配置”选择原 GPT Proxy Bridge 的 `profiles.json`，在相同用户下读取并重新加密密码，不修改旧文件。不能解密的密码需要重新填写。导出包含代理地址、用户名及软件路径，不含密码。

服务为单用户控制；安装者 SID 写入 `HKLM\SOFTWARE\AppProxyBridge\OwnerSid`。当前版本不支持多用户同时配置，也不允许界面通过服务启动任意程序。目标程序由普通权限界面启动。

## 开发与构建

要求 Windows 10/11 x64、.NET 10 SDK、PowerShell 7。便携 C 编译器 Zig 0.14.1 只放在项目 `.tools` 下，SHA256 校验后使用。

```powershell
./bootstrap.ps1
./build.ps1 -Test -UiTest
./build.ps1 -Publish -Installer -Preview
```

常规测试使用模拟代理、独立临时配置和延迟加载的原生 DLL，不安装或启用驱动，不读取个人代理凭据。WPF 测试需要交互式 Windows 桌面。预览图位于 `artifacts/previews`。

正式打包需要官方签名的 WinDivert 2.2.2-A x64 SDK，在符合本机安全策略的环境中运行 `./bootstrap.ps1 -FetchWinDivert` 获取。脚本不更改安全软件设置。正式打包命令不带 `-Preview`；缺少驱动或签名无效会停止：

```powershell
./build.ps1 -Test -UiTest -Publish -Installer
```

MSI 使用 Windows Installer 数据库和系统 makecab 创建，不自动执行安装。安装时创建本项目服务、防火墙规则和开始菜单快捷方式；卸载删除这些资源，保留用户配置。WinDivert 运行时按其官方机制加载；本项目不停止、删除或修复其他工具的共享 WinDivert 服务。

## 实现与限制

- Core：配置、上游握手、不可变本地 SOCKS5 网关、原生接口、版本化管道通信。
- Service：Windows 服务托管、单用户管道访问控制、安装辅助操作。
- App：WPF/MVVM、软件发现、MSIX 稳定身份解析、普通权限启动、主题与托盘。
- native/vendor：固定版本开源内核及适配；构建时不从网络拉取最新代码。
- Tests/UiTests：网络模拟、路径规则、凭据与协议、WPF 及服务通信回归。

每次应用代理会建立新的不可变网关，旧网关在停止时统一释放；一次连续运行最多 1024 个配置版本，达到后保留当前配置并要求停止后重新启用。实时连接最多展示 400 条，事件最近 500 条。

上游 TLS 拒绝无效证书；UDP 协商成功仅说明 ASSOCIATE 成功，实际通路需要数据往返验证。保护进程、网络共享代理服务、同一 UDP socket 多目标高并发、第三方驱动共存及长期负载仍需实机验收。MSIX 路径在界面刷新及每 30 秒检查时重新解析；界面完全退出后不会继续执行包更新发现。

具体通过项目和未验证范围见 [VERIFICATION.md](VERIFICATION.md)。项目为独立第三方工具，不提供节点、订阅或 VPN 服务。

## 许可

本项目原创部分以 [MIT License](LICENSE) 开源。复用的 ProxyBridge、GPT Proxy Bridge 代码保留其 MIT 声明；WinDivert 按其 LGPLv3/GPLv2 双重许可分发，不因本项目采用 MIT 而更改。完整说明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
