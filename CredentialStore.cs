using System.Security.Cryptography;
using System.Text.Json;

namespace CampusMobileLogin;

internal sealed record LoginCredentials(string Origin, string Username, string Password);

internal sealed class CredentialStore(string path)
{
    public LoginCredentials? Load()
    {
        if (!File.Exists(path)) return null;
        byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<LoginCredentials>(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void Save(LoginCredentials credentials)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try
        {
            byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path + ".tmp", encrypted);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void Clear() => File.Delete(path);
}
