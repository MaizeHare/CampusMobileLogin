using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace CampusMobileLogin;

// Only the configured portal origin may save or receive credentials.
internal sealed class PortalCredentials(CoreWebView2 browser, CredentialStore store, Func<string> loginUrl, Action<string> report)
{
    public bool Enabled { get; private set; }
    private string Origin => new Uri(loginUrl()).GetLeftPart(UriPartial.Authority);
    private bool IsPortal(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.GetLeftPart(UriPartial.Authority).Equals(Origin, StringComparison.OrdinalIgnoreCase);

    public async Task InstallAsync(bool enabled)
    {
        Enabled = enabled;
        browser.WebMessageReceived += OnMessage;
        await browser.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) store.Clear();
        if (IsPortal(browser.Source)) await ConfigurePageAsync();
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (!IsPortal(e.Source)) return;
            var message = JsonSerializer.Deserialize<JsonElement>(e.WebMessageAsJson);
            if (!message.TryGetProperty("kind", out var kind)) return;
            if (kind.GetString() == "campus-credentials-ready")
            {
                if (IsPortal(browser.Source)) await ConfigurePageAsync();
                return;
            }
            if (kind.GetString() != "campus-credentials-save" || !Enabled) return;
            string username = message.GetProperty("username").GetString() ?? "";
            string password = message.GetProperty("password").GetString() ?? "";
            if (username.Length == 0 || password.Length == 0) return;
            store.Save(new LoginCredentials(Origin, username, password));
            report("账号密码已在本机加密记住");
        }
        catch { report("记住账号密码失败，请取消勾选后重试。"); }
    }

    private async Task ConfigurePageAsync()
    {
        LoginCredentials? credentials = Enabled ? store.Load() : null;
        if (credentials != null && !credentials.Origin.Equals(Origin, StringComparison.OrdinalIgnoreCase)) credentials = null;
        string payload = JsonSerializer.Serialize(new { enabled = Enabled, origin = Origin, credentials });
        await browser.ExecuteScriptAsync($"window.__campusCredentialBridge?.configure({payload})");
    }

    internal const string BridgeScript = """
        (() => {
          if (window !== window.top) return;
          let enabled = false, observedPassword = null, timer;
          const fields = () => {
            const passwords = [...document.querySelectorAll('input[name="upass"]')];
            const password = passwords.find(e => e.getClientRects().length && !e.disabled);
            const scope = password?.form || document;
            const username = [...scope.querySelectorAll('input[name="DDDDD"]')]
              .find(e => e.getClientRects().length && !e.disabled);
            return username && password ? { username, password } : null;
          };
          const save = () => {
            clearTimeout(timer);
            const inputs = fields();
            if (!enabled || !inputs || !inputs.username.value || !inputs.password.value) return;
            window.chrome.webview.postMessage({kind:'campus-credentials-save',
              username:inputs.username.value, password:inputs.password.value});
          };
          const setValue = (input, value) => {
            Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,value);
            input.dispatchEvent(new Event('input',{bubbles:true}));
            input.dispatchEvent(new Event('change',{bubbles:true}));
          };
          window.__campusCredentialBridge = { configure(config) {
            if (location.origin !== config.origin) return;
            enabled = config.enabled;
            if (!enabled) { clearTimeout(timer); return; }
            const inputs = fields(), saved = config.credentials;
            if (!inputs) return;
            if (saved && (!inputs.username.value || inputs.username.value === saved.Username)) {
              // Programmatic fill should not trigger credential capture or overwrite storage.
              enabled = false;
              if (!inputs.username.value) setValue(inputs.username,saved.Username);
              if (!inputs.password.value) setValue(inputs.password,saved.Password);
              enabled = true;
            }
            save();
          }};
          const discover = () => {
            const inputs = fields();
            if (!inputs || inputs.password === observedPassword) return;
            observedPassword = inputs.password;
            window.chrome.webview.postMessage({kind:'campus-credentials-ready'});
          };
          document.addEventListener('input', e => {
            if (!enabled || !e.isTrusted || !['DDDDD','upass'].includes(e.target.name)) return;
            clearTimeout(timer); timer = setTimeout(save,500);
          },true);
          document.addEventListener('change', e => {
            if (enabled && e.isTrusted && ['DDDDD','upass'].includes(e.target.name)) save();
          },true);
          document.addEventListener('click', e => {
            if (e.isTrusted && e.target.closest('input[type="submit"],button,input[name="0MKKey"]')) save();
          },true);
          document.addEventListener('submit',save,true);
          document.addEventListener('keydown',e => { if (e.key === 'Enter' && e.isTrusted) save(); },true);
          document.addEventListener('DOMContentLoaded',discover);
          new MutationObserver(discover).observe(document,{childList:true,subtree:true});
          discover();
        })();
        """;
}
