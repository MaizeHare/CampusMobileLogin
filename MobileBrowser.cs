using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace CampusMobileLogin;

internal static class MobileBrowser
{
    public static async Task ConfigureAsync(CoreWebView2 browser, BrowserIdentity identity)
    {
        if (identity == BrowserIdentity.Computer)
        {
            await browser.CallDevToolsProtocolMethodAsync("Emulation.setUserAgentOverride", "{\"userAgent\":\"\"}");
            await browser.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}");
            await browser.CallDevToolsProtocolMethodAsync("Emulation.setTouchEmulationEnabled", "{\"enabled\":false}");
            return;
        }
        bool iphone = identity == BrowserIdentity.IPhone;
        string version = Regex.Match(browser.Environment.BrowserVersionString, @"^\d+\.\d+\.\d+\.\d+").Value;
        if (version.Length == 0)
            throw new InvalidOperationException("无法识别 WebView2 浏览器版本。");
        string major = version.Split('.')[0];
        string ua = iphone
            ? "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1"
            : $"Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{major}.0.0.0 Mobile Safari/537.36";
        await browser.CallDevToolsProtocolMethodAsync("Emulation.setUserAgentOverride", JsonSerializer.Serialize(new
        {
            userAgent = ua,
            platform = iphone ? "iPhone" : "Linux armv81",
            userAgentMetadata = new
            {
                brands = new[] { new { brand = "Chromium", version = major }, new { brand = "Google Chrome", version = major } },
                fullVersionList = new[] { new { brand = "Chromium", version }, new { brand = "Google Chrome", version } },
                fullVersion = version,
                platform = iphone ? "iOS" : "Android",
                platformVersion = iphone ? "17.4.0" : "13.0.0",
                architecture = "arm",
                model = iphone ? "iPhone" : "Pixel 7",
                mobile = true,
                bitness = "64",
                wow64 = false
            }
        }));
        await browser.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", JsonSerializer.Serialize(new
        {
            width = iphone ? 390 : 412, height = iphone ? 844 : 820,
            deviceScaleFactor = iphone ? 3.0 : 2.625, mobile = true,
            screenWidth = iphone ? 390 : 412, screenHeight = iphone ? 844 : 915
        }));
        await browser.CallDevToolsProtocolMethodAsync("Emulation.setTouchEmulationEnabled", "{\"enabled\":true,\"maxTouchPoints\":5}");
    }
}
