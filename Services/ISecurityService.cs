namespace LocalSocialArchive.Services;

public interface ISecurityService
{
    bool IsPasswordProtectionEnabled { get; set; }
    bool HasPasswordSet { get; }
    bool VerifyPassword(string password);
    void SetPassword(string newPassword);
    void RemovePassword();
}
