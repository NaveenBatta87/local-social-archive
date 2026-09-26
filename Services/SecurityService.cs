using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalSocialArchive.Services;

public class SecurityService : ISecurityService
{
    private readonly string _securityConfigPath;
    private SecurityConfig _config = new();

    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public SecurityService()
    {
        string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string baseDir = Path.Combine(userDocs, "LocalSocialArchive");
        Directory.CreateDirectory(baseDir);
        _securityConfigPath = Path.Combine(baseDir, "vault_security.json");

        LoadConfig();
    }

    public bool IsPasswordProtectionEnabled
    {
        get => _config.IsEnabled && HasPasswordSet;
        set
        {
            _config.IsEnabled = value;
            SaveConfig();
        }
    }

    public bool HasPasswordSet => !string.IsNullOrWhiteSpace(_config.PasswordHash) && !string.IsNullOrWhiteSpace(_config.Salt);

    public bool VerifyPassword(string password)
    {
        if (!HasPasswordSet) return true;
        if (string.IsNullOrEmpty(password)) return false;

        try
        {
            byte[] salt = Convert.FromBase64String(_config.Salt);
            byte[] expectedHash = Convert.FromBase64String(_config.PasswordHash);

            byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        catch
        {
            return false;
        }
    }

    public void SetPassword(string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            RemovePassword();
            return;
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(newPassword),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        _config.Salt = Convert.ToBase64String(salt);
        _config.PasswordHash = Convert.ToBase64String(hash);
        _config.IsEnabled = true;
        _config.LastUpdated = DateTime.Now;

        SaveConfig();
    }

    public void RemovePassword()
    {
        _config.PasswordHash = string.Empty;
        _config.Salt = string.Empty;
        _config.IsEnabled = false;
        _config.LastUpdated = DateTime.Now;

        SaveConfig();
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_securityConfigPath))
            {
                string json = File.ReadAllText(_securityConfigPath);
                var loaded = JsonSerializer.Deserialize<SecurityConfig>(json);
                if (loaded != null)
                {
                    _config = loaded;
                }
            }
        }
        catch
        {
            _config = new SecurityConfig();
        }
    }

    private void SaveConfig()
    {
        try
        {
            string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_securityConfigPath, json);
        }
        catch { }
    }

    private class SecurityConfig
    {
        public bool IsEnabled { get; set; } = false;
        public string PasswordHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
