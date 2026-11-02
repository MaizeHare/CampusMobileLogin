using System.Text.Json;
using System.Text.Json.Serialization;

namespace CampusMobileLogin;

[JsonConverter(typeof(JsonStringEnumConverter<BrowserIdentity>))]
internal enum BrowserIdentity { Android, IPhone, Computer }

internal sealed record AppSettings(string LoginUrl = "http://192.168.200.2/", BrowserIdentity Identity = BrowserIdentity.Android, bool RememberCredentials = true)
{
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new AppSettings();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("设置文件为空。");
        if (!Enum.IsDefined(settings.Identity)) throw new InvalidDataException("设置文件中的登录身份无效。");
        return settings;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
