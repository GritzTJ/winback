using System.Reflection;
using System.Security.Cryptography;
using WinBack.Core.Services;
using Xunit;

namespace WinBack.Tests;

/// <summary>
/// Tests unitaires pour le chiffrement AES-256 de WinBack.
/// DeriveKey est maintenant public dans RestoreEngine (accessible directement).
/// CopyAndEncryptFileAsync reste privée dans BackupEngine (accès par réflexion).
/// </summary>
public class BackupEngineEncryptionTests
{
    // ── DeriveKey ──────────────────────────────────────────────────────────────

    [Fact]
    public void DeriveKey_IsDeterministic()
    {
        // La même clé doit être produite pour le même mot de passe (déterministe)
        var key1 = RestoreEngine.DeriveKey("motdepasse");
        var key2 = RestoreEngine.DeriveKey("motdepasse");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_ProducesCorrectLength()
    {
        // SHA-256 → 32 octets = taille de clé AES-256
        var key = RestoreEngine.DeriveKey("test");
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void DeriveKey_DiffersWithDifferentPasswords()
    {
        var key1 = RestoreEngine.DeriveKey("password1");
        var key2 = RestoreEngine.DeriveKey("password2");
        Assert.NotEqual(key1, key2);
    }

    // ── CopyAndEncryptFileAsync / roundtrip ────────────────────────────────────

    [Fact]
    public async Task EncryptedFile_StartsWithIV_AndIsLongerThanSource()
    {
        var sourceContent = "Hello WinBack 0.3.1!"u8.ToArray();
        var sourceFile = Path.GetTempFileName();
        var encryptedFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllBytesAsync(sourceFile, sourceContent);

            var key = RestoreEngine.DeriveKey("password");
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
        // Vérifier le roundtrip complet :
        //  - Chiffrement via BackupEngine.CopyAndEncryptFileAsync (méthode privée, accès par réflexion)
        //  - Clé dérivée via RestoreEngine.DeriveKey (méthode publique)
        //  - Déchiffrement manuel AES-256-CBC pour valider le format produit
        var original = "Données de test WinBack — chiffrement AES-256"u8.ToArray();
        var sourceFile = Path.GetTempFileName();
        var encryptedFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllBytesAsync(sourceFile, original);

            var key = RestoreEngine.DeriveKey("my-secret-password");
            await InvokeCopyAndEncryptFileAsync(sourceFile, encryptedFile, key, CancellationToken.None);

            // Format attendu : 16 octets IV (en clair) + données AES-256-CBC
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

    [Fact]
    public async Task RestoreEngine_DecryptsCorrectly_Roundtrip()
    {
        // Test end-to-end : BackupEngine chiffre, RestoreEngine déchiffre via RestoreAsync
        var original = "Fichier WinBack restauré sur laptop 2"u8.ToArray();
        var sourceDir = Path.Combine(Path.GetTempPath(), "WB_EncSrc_" + Guid.NewGuid());
        var destDir   = Path.Combine(Path.GetTempPath(), "WB_EncDst_" + Guid.NewGuid());

        try
        {
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(destDir);

            var encryptedFile = Path.Combine(sourceDir, "test.txt");
            var rawFile = Path.GetTempFileName();
            await File.WriteAllBytesAsync(rawFile, original);

            var key = RestoreEngine.DeriveKey("motdepasse-cross-machine");
            await InvokeCopyAndEncryptFileAsync(rawFile, encryptedFile, key, CancellationToken.None);
            File.Delete(rawFile);

            // Restaurer avec RestoreEngine
            var engine = new RestoreEngine(Microsoft.Extensions.Logging.Abstractions.NullLogger<RestoreEngine>.Instance);
            var result = await engine.RestoreAsync(
                new RestoreEngine.RestoreOptions(
                    SourceFolder: sourceDir,
                    DestinationFolder: destDir,
                    IsEncrypted: true,
                    DecryptionKey: key),
                ct: CancellationToken.None);

            Assert.Equal(0, result.Errored);
            Assert.Equal(1, result.Restored);

            var restoredContent = await File.ReadAllBytesAsync(Path.Combine(destDir, "test.txt"));
            Assert.Equal(original, restoredContent);
        }
        finally
        {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, recursive: true);
            if (Directory.Exists(destDir))   Directory.Delete(destDir, recursive: true);
        }
    }

    // ── Helper via réflexion (CopyAndEncryptFileAsync reste privée dans BackupEngine) ─────

    private static Task InvokeCopyAndEncryptFileAsync(
        string source, string dest, byte[] key, CancellationToken ct)
    {
        var method = typeof(BackupEngine)
            .GetMethod("CopyAndEncryptFileAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("CopyAndEncryptFileAsync introuvable dans BackupEngine");
        return (Task)method.Invoke(null, [source, dest, key, ct])!;
    }
}
