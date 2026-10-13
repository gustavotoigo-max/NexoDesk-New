using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using NexoDesk.Shared.Configuration;
using NexoDesk.Shared.Security;
using NexoDesk.Shared.Session;

namespace NexoDesk.Desktop;

/// <summary>Estado compartilhado entre a interface elevada e o agente SYSTEM.</summary>
public sealed class MachineState : IClientStore, IDisposable
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NexoDesk-New");
    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");
    public static string Logs => Path.Combine(DataDirectory, "logs");
    public AppConfig Config { get; private set; }
    public X509Certificate2 Certificate { get; }

    public MachineState(bool provision = false)
    {
        SecureDirectory();
        using FileStream configLock = AcquireLock();
        Config = ConfigStore.Load(ConfigFile) ?? new AppConfig
        {
            DeviceName = Environment.MachineName, DeviceId = AppConfig.MakeId(Environment.MachineName),
        };
        if (Config.SchemaVersion != AppConfig.CurrentSchema)
            throw new ConfigException("Configuração incompatível. Preserve o arquivo e consulte a documentação.");
        X509Certificate2? cert = FindMachineCertificate(Config.CertificateSha256);
        if (cert is null)
        {
            if (!provision || !string.IsNullOrEmpty(Config.CertificateSha256))
                throw new ConfigException("Identidade da máquina ausente. Repare a instalação e refaça os pareamentos.");
            cert = DeviceIdentity.Create(Config.DeviceName, location: StoreLocation.LocalMachine);
            RestrictPrivateKey(cert);
            Config.CertificateSha256 = CertificateFingerprint.Sha256(cert);
            ConfigStore.Save(ConfigFile, Config);
        }
        Certificate = cert;
    }

    public void Reload() => Config = ConfigStore.Load(ConfigFile) ?? throw new ConfigException("Configuração ausente.");

    public void Update(Action<AppConfig> update)
    {
        using FileStream configLock = AcquireLock();
        AppConfig latest = ConfigStore.Load(ConfigFile) ?? Config;
        update(latest);
        ConfigStore.Save(ConfigFile, latest);
        Config = latest;
    }

    public AuthorizedClient? Find(string fingerprint)
    {
        Reload();
        string? fp = CertificateFingerprint.Normalize(fingerprint);
        PairedAdmin? admin = Config.Remote.Admins.FirstOrDefault(a => CertificateFingerprint.Normalize(a.Sha256) == fp);
        return admin is null || fp is null ? null : new(admin.Name, fp);
    }

    public void Add(AuthorizedClient client) => Update(c =>
    {
        c.Remote.Admins.RemoveAll(a => CertificateFingerprint.Normalize(a.Sha256) == client.Fingerprint);
        c.Remote.Admins.Add(new() { Name = client.Name, Sha256 = client.Fingerprint, PairedAt = DateTimeOffset.UtcNow });
    });

    public static void SecureDirectory()
    {
        var directory = new DirectoryInfo(DataDirectory);
        if (!directory.Exists) directory.Create();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (WellKnownSidType sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    private static FileStream AcquireLock()
    {
        for (int i = 0; ; i++)
        {
            try { return new FileStream(Path.Combine(DataDirectory, "config.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 100) { Thread.Sleep(25); }
        }
    }

    private static X509Certificate2? FindMachineCertificate(string fingerprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        X509Certificate2? result = null;
        foreach (X509Certificate2 cert in store.Certificates)
        {
            if (result is null && cert.HasPrivateKey && CertificateFingerprint.Sha256(cert) == fingerprint) result = cert;
            else cert.Dispose();
        }
        return result;
    }

    private static void RestrictPrivateKey(X509Certificate2 certificate)
    {
        using ECDsa? key = certificate.GetECDsaPrivateKey();
        if (key is not ECDsaCng cng) throw new CryptographicException("Provedor de chave não suportado.");
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Crypto", "Keys", cng.Key.UniqueName!);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (WellKnownSidType sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    public void Dispose() => Certificate.Dispose();
}

public static class PasswordVault
{
    private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("NexoDesk-New/password/v1");
    public static string Protect(string password) => Convert.ToBase64String(ProtectedData.Protect(
        System.Text.Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));
    public static string? Unprotect(string? value)
    {
        if (value is null) return null;
        try
        {
            byte[] plaintext = ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser);
            try { return System.Text.Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }
}