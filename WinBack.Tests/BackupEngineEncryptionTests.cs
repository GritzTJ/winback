using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace WinBack.Tests;

public class BackupEngineEncryptionTests
{
    // ── DeriveKey ──────────────────────────────────────────────────────────────

    [Fact]
    public void DeriveKey_IsDeterministic()
    {
        var key1 = InvokeDeriveKey("motdepasse");
        var key2 = InvokeDeriveKey("motdepasse");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_ProducesCorrectLength()
    {
        var key = InvokeDeriveKey("test");
        Assert.Equal(32, key.Length); // AES-256 → 32 octets
    }

    [Fact]
    public void DeriveKey_DiffersWithDifferentPasswords()
    {
        var key1 = InvokeDeriveKey("password1");
        var key2 = InvokeDeriveKey("password2");
        Assert.NotEqual(key1, key2);
    }

    // ── CopyAndEncryptFileAsync / roundtrip ────────────────────────────────────

    [Fact]
    public async Task EncryptedFile_StartsWithIV_AndIsLongerThanSource()
    {
        var sourceContent = "Hello WinBack 0.3.0!"u8.ToArray();
        var sourceFile = Path.GetTempFileName();
        var encryptedFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllBytesAsync(sourceFile, sourceContent);

            var key = InvokeDeriveKey("password");
            await InvokeCopyAndEncryptFileAsync(sourceFile, encryptedFile, key, CancellationToken.None);

            var encrypted = await File.ReadAllBytesAsync(encryptedFile);

            // Les 16 premiers octets sont l'IV
            Assert.True(encrypted.Length >= 16, "Le fichier chiffré doit contenir au moins 16 octets (IV)");
            // Le fichier chiffré est plus grand que la source (IV + données chiffrées + padding)
            Assert.True(encrypted.Length > sourceContent.Length);
        }
        finally
        {
            File.Delete(sourceFile);
            File.Delete(encryptedFile);
        }
    }

    [Fact]
    public async Task EncryptDecrypt_Roundtrip()
    {
        var original = "Données de test WinBack — chiffrement AES-256"u8.ToArray();
        var sourceFile = Path.GetTempFileName();
        var encryptedFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllBytesAsync(sourceFile, original);

            var key = InvokeDeriveKey("my-secret-password");
            await InvokeCopyAndEncryptFileAsync(sourceFile, encryptedFile, key, CancellationToken.None);

            // Déchiffrement manuel pour vérifier le roundtrip
            var encryptedBytes = await File.ReadAllBytesAsync(encryptedFile);
            var iv = encryptedBytes[..16];
            var cipherText = encryptedBytes[16..];

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var ms = new MemoryStream(cipherText);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var result = new MemoryStream();
            await cs.CopyToAsync(result);

            Assert.Equal(original, result.ToArray());
        }
        finally
        {
            File.Delete(sourceFile);
            File.Delete(encryptedFile);
        }
    }

    // ── Helpers via reflection (méthodes privées statiques de BackupEngine) ─────

    private static byte[] InvokeDeriveKey(string password)
    {
        var method = typeof(WinBack.Core.Services.BackupEngine)
            .GetMethod("DeriveKey", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("DeriveKey introuvable");
        return (byte[])method.Invoke(null, [password])!;
    }

    private static Task InvokeCopyAndEncryptFileAsync(
        string source, string dest, byte[] key, CancellationToken ct)
    {
        var method = typeof(WinBack.Core.Services.BackupEngine)
            .GetMethod("CopyAndEncryptFileAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("CopyAndEncryptFileAsync introuvable");
        return (Task)method.Invoke(null, [source, dest, key, ct])!;
    }
}
