using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;

namespace CampusMobileLogin;

internal static class CredentialSmokeTest
{
    public static async Task<bool> RunAsync(WebView2 browser, string directory, string origin)
    {
        string path = Path.Combine(directory, "test-credentials.bin");
        var store = new CredentialStore(path);
        var sample = new LoginCredentials(origin.TrimEnd('/'), "test-student", "test-password-123");
        try
        {
            store.Save(sample);
            bool roundtrip = new CredentialStore(path).Load() == sample;
            string encrypted = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            bool encryptedAtRest = !encrypted.Contains(sample.Password) && !encrypted.Contains(sample.Username);
            // Use a fresh script bridge on the real local page. No school credentials are used.
            await browser.CoreWebView2.ExecuteScriptAsync("document.body.innerHTML = '<form><input name=DDDDD><input name=upass type=password><input name=0MKKey type=submit value=登录></form>'");
            await browser.CoreWebView2.ExecuteScriptAsync(PortalCredentials.BridgeScript);
            var portal = new PortalCredentials(browser.CoreWebView2, store, () => origin, _ => { });
            await portal.InstallAsync(true);
            await portal.SetEnabledAsync(true);
            bool filled = await IsTrueAsync(browser, "document.querySelector('[name=DDDDD]').value === 'test-student' && document.querySelector('[name=upass]').value === 'test-password-123'");
            // Keep a prefilled account, and fill only its empty password.
            await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[name=upass]').value = ''");
            await portal.SetEnabledAsync(true);
            bool prefilledAccountOk = await IsTrueAsync(browser, "document.querySelector('[name=upass]').value === 'test-password-123'");
            // A different portal origin must not receive the saved password.
            store.Save(sample with { Origin = "https://other.example" });
            await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[name=DDDDD]').value = ''; document.querySelector('[name=upass]').value = ''");
            await portal.SetEnabledAsync(true);
            bool differentOriginBlocked = await IsTrueAsync(browser, "document.querySelector('[name=upass]').value === ''");
            store.Save(sample);
            await portal.SetEnabledAsync(false);
            bool cleared = !File.Exists(path);
            // A disabled bridge must not capture even a submitted form.
            await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[name=DDDDD]').value='test-student';document.querySelector('[name=upass]').value='test-password-123';document.querySelector('form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))");
            await browser.CoreWebView2.ExecuteScriptAsync("true");
            bool disabledCaptureBlocked = !File.Exists(path);
            await portal.SetEnabledAsync(true);
            await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!File.Exists(path) && timer.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(25);
            bool captured = store.Load() == sample;
            await portal.SetEnabledAsync(false);
            return roundtrip && encryptedAtRest && filled && prefilledAccountOk && differentOriginBlocked && cleared && disabledCaptureBlocked && captured;
        }
        finally { store.Clear(); }
    }

    private static async Task<bool> IsTrueAsync(WebView2 browser, string script) =>
        JsonSerializer.Deserialize<bool>(await browser.CoreWebView2.ExecuteScriptAsync(script));
}
