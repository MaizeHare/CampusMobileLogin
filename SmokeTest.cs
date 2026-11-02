using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CampusMobileLogin;

internal static class SmokeTest
{
    public static async Task RunAsync(WebView2 browser, string output, string dataDirectory, Func<bool> checkTray)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        string origin = $"http://127.0.0.1:{port}/";
        using var server = new HttpListener();
        server.Prefixes.Add(origin);
        server.Start();
        using var stop = new CancellationTokenSource();
        var requests = new ConcurrentQueue<object>();
        Task serving = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await server.GetContextAsync().WaitAsync(stop.Token); }
                catch (OperationCanceledException) { break; }
                requests.Enqueue(new
                {
                    path = context.Request.RawUrl,
                    ua = context.Request.UserAgent,
                    mobile = context.Request.Headers["Sec-CH-UA-Mobile"],
                    platform = context.Request.Headers["Sec-CH-UA-Platform"]
                });
                const string html = """
                    <!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <script>window.earlyIdentity={ua:navigator.userAgent,platform:navigator.userAgentData.platform,mobile:navigator.userAgentData.mobile,touch:navigator.maxTouchPoints,width:innerWidth};</script>
                    <title>校园网身份登录自检</title></head><body style="font-family:system-ui;margin:28px">
                    <h2>校园网身份登录</h2><p>正在验证浏览器身份、凭据记忆与托盘功能。</p>
                    <p>这是本机测试页，不会连接校园网或提交真实账号密码。</p></body></html>
                    """;
                byte[] content = Encoding.UTF8.GetBytes(html);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = content.Length;
                await context.Response.OutputStream.WriteAsync(content);
                context.Response.Close();
            }
        });
        try
        {
            bool trayOk = checkTray();
            await MobileBrowser.ConfigureAsync(browser.CoreWebView2, BrowserIdentity.Computer);
            int nativeTouchPoints = JsonSerializer.Deserialize<int>(await browser.CoreWebView2.ExecuteScriptAsync("navigator.maxTouchPoints"));
            var results = new List<object>();
            bool identitiesOk = true;
            bool persistenceOk = true;
            string settingsPath = Path.Combine(dataDirectory, "self-test-settings.json");
            BrowserIdentity[] sequence = [BrowserIdentity.Android, BrowserIdentity.IPhone, BrowserIdentity.Computer, BrowserIdentity.Android];
            foreach (var mode in sequence)
            {
                await MobileBrowser.ConfigureAsync(browser.CoreWebView2, mode);
                var saved = new AppSettings(origin, mode);
                saved.Save(settingsPath);
                persistenceOk &= AppSettings.Load(settingsPath) == saved;
                await NavigateAsync(browser, () => browser.CoreWebView2.Navigate(origin + mode));
                var first = await ReadIdentityAsync(browser);
                await NavigateAsync(browser, () => browser.Reload());
                var reloaded = await ReadIdentityAsync(browser);
                bool mobile = mode != BrowserIdentity.Computer;
                string expectedPlatform = mode switch { BrowserIdentity.Android => "Android", BrowserIdentity.IPhone => "iOS", _ => "Windows" };
                string expectedUa = mode switch { BrowserIdentity.Android => "Android 13; Pixel 7", BrowserIdentity.IPhone => "iPhone; CPU iPhone OS", _ => "Windows NT" };
                bool valid = new[] { first, reloaded }.All(v =>
                    v.GetProperty("ua").GetString()!.Contains(expectedUa) &&
                    v.GetProperty("mobile").GetBoolean() == mobile &&
                    v.GetProperty("platform").GetString() == expectedPlatform &&
                    v.GetProperty("touch").GetInt32() == (mobile ? 5 : nativeTouchPoints) &&
                    (!mobile || v.GetProperty("width").GetInt32() == (mode == BrowserIdentity.Android ? 412 : 390)));
                identitiesOk &= valid;
                results.Add(new { mode = mode.ToString(), passed = valid, first, reloaded });
            }
            JsonElement headers = JsonSerializer.SerializeToElement(requests.ToArray());
            bool headersOk = headers.GetArrayLength() >= 8 && headers.EnumerateArray().All(v =>
            {
                string ua = v.GetProperty("ua").GetString()!;
                string expectedPlatform = ua.Contains("Android") ? "\"Android\"" : ua.Contains("iPhone") ? "\"iOS\"" : "\"Windows\"";
                return v.GetProperty("mobile").GetString() == (ua.Contains("Windows NT") ? "?0" : "?1") &&
                    v.GetProperty("platform").GetString() == expectedPlatform;
            });
            bool credentialsOk = await CredentialSmokeTest.RunAsync(browser, dataDirectory, origin);
            await using (var screenshot = File.Create(Path.ChangeExtension(output, ".png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
            bool passed = identitiesOk && headersOk && persistenceOk && trayOk && credentialsOk;
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed, identitiesOk, headersOk, persistenceOk, trayOk, credentialsOk, results, requests = headers }, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = passed ? 0 : 1;
        }
        finally
        {
            stop.Cancel();
            server.Stop();
            await serving;
        }
    }

    private static async Task<JsonElement> ReadIdentityAsync(WebView2 browser) =>
        JsonSerializer.Deserialize<JsonElement>(await browser.CoreWebView2.ExecuteScriptAsync("window.earlyIdentity"));

    private static async Task NavigateAsync(WebView2 browser, Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess) completion.TrySetResult();
            else completion.TrySetException(new InvalidOperationException("自检页面加载失败：" + e.WebErrorStatus));
        }
        browser.NavigationCompleted += Completed;
        try { action(); await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { browser.NavigationCompleted -= Completed; }
    }
}
