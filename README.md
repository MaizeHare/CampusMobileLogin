# 校园网身份登录

> 本项目仅供个人学习、研究及非商业交流使用。未经作者书面许可，严禁出售、付费分发、收费服务及其他商业或营利用途。使用、修改或分享时，请保留作者署名与授权声明。
>
> 完整授权条件见 [LICENSE](LICENSE)。第三方代码、图标与依赖遵循各自的许可，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

基于 Windows Forms 和 Microsoft Edge WebView2 的校园网登录工具。支持选择并记住 Android、iPhone 或 Windows 浏览器身份、加密记忆账号密码，以及最小化到托盘。

## 使用

1. 先让笔记本连接校园网 Wi-Fi。
2. 双击 `CampusMobileLogin.exe`，窗口会自动使用上次选择的身份打开校园网认证地址。首次默认为 Android / Pixel 7。
3. 顶部“登录身份”可选择 Android 手机、iPhone 手机或电脑（Windows）。切换后立即刷新当前页面，并自动保存选择，下次打开不必重选。
4. 默认勾选“记住账号密码（本机加密）”。在校园网原登录页输入一次账号密码，工具会自动记住，下次打开自动填入。仍由你点击网页的“登录”。取消勾选会删除工具保存的账号密码；这个选择也会记住。
5. 若要改变校园网已登记的设备类型，请先在校园网页退出认证，再切换身份重新登录；只切换显示身份不会主动注销已有会话。不需要打开 Edge 开发者工具或安装浏览器扩展。
6. 点击窗口右上角最小化按钮，窗口会收进右下角系统托盘，从任务栏隐藏。托盘图标可能在“^”展开列表中，单击图标即可恢复；右键可选“显示窗口”或“退出”。点击窗口关闭按钮会退出程序。
7. 登录后可正常使用其他浏览器和软件。如果关闭工具后学校仍保持认证，就无需继续开着；是否需要重新认证，以学校的会话策略为准。

地址栏是可输入的下拉框，固定保留 `http://192.168.200.2/`。登录成功跳转到其他页面后，点击地址栏右侧下拉箭头，选中该地址即可直接打开校园网入口，不用再手动复制。上次保存的自定义登录地址也会出现在列表中。若页面不能正常打开，可以在地址栏粘贴完整认证地址，按回车打开；手动打开的地址会在本机记住。右侧“刷新”按钮用于重新加载当前页面。

工具只改变登录窗口的浏览器身份，不会修改 Windows、MAC 地址、网卡设置或电脑其他浏览器。iPhone 选项模拟设备标识与视口，浏览器内核仍是 WebView2。

账号密码记忆针对当前学校 Dr.COM 登录页的 `DDDDD` / `upass` 字段。仅在勾选时从配置的校园网来源读取，使用 Windows DPAPI（CurrentUser）加密写入本机 `credentials.bin`，并且只向保存时同一来源（协议、主机、端口相同）的登录页填入。工具不自动提交登录，不把账号密码写入普通设置或日志。换一个网站不会收到保存的密码。

身份选择、认证地址、记忆开关、加密凭据，以及独立浏览器的 Cookie 和网页存储保存在 `%LOCALAPPDATA%\CampusMobileLogin\UserData`，关闭后保留。取消勾选删除的是工具的加密凭据；校园网页自身的 Cookie 或“保存密码”选项由网页管理。

是否被归类为移动终端、是否允许两台电脑同时在线，需要在校园网自助服务中确认。这个工具不自动重拨，也不承诺学校修改识别规则后仍有效。

## 运行环境

Windows x64、[.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)（.NET Desktop Runtime）和 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。请保留发布目录中的所有文件，不能只拷贝 exe。

## 源码与构建

在 Windows 上安装 .NET 8 或更新版本的 SDK，然后克隆仓库并进入项目目录：

```powershell
git clone https://github.com/MaizeHare/CampusMobileLogin.git
cd CampusMobileLogin
```

正式程序统一运行 `release\CampusMobileLogin.exe`，用户设置和加密凭据继续保存在上述用户数据目录。

`release` 是唯一用于日常运行和打包分发的目录。`bin` 和 `obj` 是 .NET 编译时自动生成的输出和缓存，可删除，下次构建会重新生成。

使用 WinForms、微软官方 Microsoft.Web.WebView2 SDK，手机设置通过 Chromium 的 Emulation 协议在首次导航前应用，包含 UA、Client Hints、触摸和手机视口。

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o release
```

Git 仓库只包含源码、项目配置、图标和授权文件。构建产物、分发压缩包、浏览器数据和个人凭据均由 `.gitignore` 排除。

协议参考：https://chromedevtools.github.io/devtools-protocol/tot/Emulation/
WebView2：https://learn.microsoft.com/microsoft-edge/webview2/
