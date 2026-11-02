using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CampusMobileLogin;

internal sealed class LoginForm : Form
{
    private readonly string dataDirectory;
    private readonly string? testOutput;
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private readonly ComboBox address = new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems,
        DropDownWidth = 600, Enabled = false
    };
    private readonly Button refresh = new() { Text = "刷新", AutoSize = true, Enabled = false };
    private readonly ComboBox identity = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Enabled = false };
    private readonly CheckBox remember = new() { Text = "记住账号密码（本机加密）", AutoSize = true, Enabled = false };
    private readonly NotifyIcon tray = new() { Text = "校园网身份登录" };
    private readonly Icon applicationIcon;
    private readonly ContextMenuStrip trayMenu = new();
    private PortalCredentials? portalCredentials;
    private FormWindowState restoredState = FormWindowState.Normal;
    private AppSettings settings = new();
    private string SettingsPath => Path.Combine(dataDirectory, "settings.json");
    private string IdentityLabel => identity.SelectedItem?.ToString() ?? "";
    private readonly Label status = new() { Dock = DockStyle.Fill, Text = "正在准备手机模式…", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };

    public LoginForm(string? testOutput)
    {
        this.testOutput = testOutput;
        using var iconStream = typeof(LoginForm).Assembly.GetManifestResourceStream("CampusMobileLogin.AppIcon")
            ?? throw new InvalidOperationException("缺少应用图标资源。");
        using var sourceIcon = new Icon(iconStream);
        applicationIcon = (Icon)sourceIcon.Clone();
        Icon = applicationIcon;
        tray.Icon = applicationIcon;
        tray.Visible = true;
        dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CampusMobileLogin", testOutput == null ? "UserData" : "SelfTestData");
        Text = "校园网身份登录";
        Font = new Font("Microsoft YaHei UI", 10);
        ClientSize = new Size(560, 900);
        MinimumSize = new Size(490, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 247, 250);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var title = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        title.Controls.Add(new Label { Text = "登录身份", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        identity.Items.AddRange(["Android 手机（Pixel 7）", "iPhone 手机", "电脑（Windows）"]);
        title.Controls.Add(identity);
        title.Controls.Add(new Label { Text = "自动记住上次选择", AutoSize = true, ForeColor = Color.FromArgb(20, 100, 70), Padding = new Padding(6, 6, 0, 0) });
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        address.Items.Add("http://192.168.200.2/");
        toolbar.Controls.Add(address, 0, 0);
        toolbar.Controls.Add(refresh, 1, 0);
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(remember, 0, 1);
        layout.Controls.Add(toolbar, 0, 2);
        layout.Controls.Add(browser, 0, 3);
        layout.Controls.Add(status, 0, 4);
        Controls.Add(layout);

        address.SelectionChangeCommitted += (_, _) =>
        {
            address.Text = address.SelectedItem?.ToString() ?? address.Text;
            Navigate();
        };
        refresh.Click += (_, _) => browser.Reload();
        identity.SelectionChangeCommitted += async (_, _) => await ChangeIdentityAsync();
        remember.CheckedChanged += async (_, _) => await ChangeRememberAsync();
        address.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Navigate(); } };
        Shown += async (_, _) => await InitializeAsync();
        trayMenu.Items.Add("显示窗口", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("退出", null, (_, _) => Close());
        tray.ContextMenuStrip = trayMenu;
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized) { Hide(); ShowInTaskbar = false; }
            else restoredState = WindowState;
        };
        FormClosed += (_, _) => { tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); browser.Dispose(); applicationIcon.Dispose(); };
    }

    private async Task InitializeAsync()
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            settings = AppSettings.Load(SettingsPath);
            if (!address.Items.Contains(settings.LoginUrl)) address.Items.Add(settings.LoginUrl);
            identity.SelectedIndex = (int)settings.Identity;
            remember.Checked = settings.RememberCredentials;
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDirectory);
            await browser.EnsureCoreWebView2Async(environment);
            // Apply all identity overrides before the first real navigation or page script.
            await MobileBrowser.ConfigureAsync(browser.CoreWebView2, settings.Identity);
            browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            portalCredentials = new PortalCredentials(browser.CoreWebView2,
                new CredentialStore(Path.Combine(dataDirectory, "credentials.bin")), () => settings.LoginUrl,
                message => status.Text = message);
            await portalCredentials.InstallAsync(settings.RememberCredentials);
            browser.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (IsWebAddress(e.Uri)) browser.CoreWebView2.Navigate(e.Uri);
            };
            browser.CoreWebView2.NavigationStarting += (_, _) => status.Text = IdentityLabel + " · 正在打开页面…";
            browser.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (testOutput == null) address.Text = browser.Source?.AbsoluteUri ?? address.Text;
                status.Text = e.IsSuccess ? IdentityLabel + " · 请在上方页面登录" : "页面未打开：" + e.WebErrorStatus + "，请检查校园网连接和地址。";
            };
            refresh.Enabled = identity.Enabled = remember.Enabled = address.Enabled = true;
            if (testOutput != null)
            {
                await SmokeTest.RunAsync(browser, testOutput, dataDirectory, CheckTrayCycle);
                Close();
                return;
            }
            address.Text = settings.LoginUrl;
            Navigate(save: false);
        }
        catch (Exception ex)
        {
            status.Text = "启动失败：" + ex.Message;
            if (testOutput != null)
            {
                File.WriteAllText(testOutput, System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
                Environment.ExitCode = 1;
                Close();
            }
            else MessageBox.Show(this, "无法启动手机登录窗口。\n\n" + ex.Message, "校园网手机登录", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool IsWebAddress(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = restoredState;
        Activate();
    }

    private bool CheckTrayCycle()
    {
        WindowState = FormWindowState.Minimized;
        bool hidden = !Visible && !ShowInTaskbar && tray.Visible;
        RestoreFromTray();
        return hidden && Visible && ShowInTaskbar && WindowState != FormWindowState.Minimized;
    }

    private async Task ChangeRememberAsync()
    {
        if (!remember.Enabled || portalCredentials == null) return;
        remember.Enabled = false;
        try
        {
            settings = settings with { RememberCredentials = remember.Checked };
            settings.Save(SettingsPath);
            await portalCredentials.SetEnabledAsync(remember.Checked);
            status.Text = remember.Checked ? "将在本机加密记住账号密码，下次自动填入" : "已清除工具保存的账号密码";
        }
        catch { status.Text = "保存账号密码设置失败，请重试。"; }
        finally { remember.Enabled = true; }
    }

    private async Task ChangeIdentityAsync()
    {
        address.Enabled = refresh.Enabled = identity.Enabled = false;
        try
        {
            var selected = (BrowserIdentity)identity.SelectedIndex;
            await MobileBrowser.ConfigureAsync(browser.CoreWebView2, selected);
            settings = settings with { Identity = selected };
            settings.Save(SettingsPath);
            browser.Reload();
            address.Enabled = refresh.Enabled = true;
        }
        catch (Exception ex) { status.Text = "切换失败：" + ex.Message; }
        finally { identity.Enabled = true; }
    }

    private void Navigate(bool save = true)
    {
        if (browser.CoreWebView2 == null || !address.Enabled) return;
        string url = address.Text.Trim();
        if (!url.Contains("://")) url = "http://" + url;
        if (!IsWebAddress(url))
        {
            status.Text = "请输入有效的 http:// 或 https:// 认证地址。";
            return;
        }
        try
        {
            if (save)
            {
                settings = settings with { LoginUrl = url };
                settings.Save(SettingsPath);
            }
            browser.CoreWebView2.Navigate(url);
        }
        catch (Exception ex) { status.Text = "打开失败：" + ex.Message; }
    }
}
