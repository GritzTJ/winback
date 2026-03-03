using System.Reflection;
using System.Security.Cryptography;
using WinBack.Core.Services;
using Xunit;

namespace WinBack.Tests;

/// <summary>
/// Tests unitaires pour le chiffrement AES-256 de WinBack.
/// DeriveKey/DeriveKeyV2 sont publics dans RestoreEngine.
/// CopyAndEncryptFileAsync reste privée dans BackupEngine (accès par réflexion).
/// </summary>
public class BackupEngineEncryptionTests
{
    // ── DeriveKey (legacy SHA-256) ──────────────────────────────────────────────

    [Fact]
    public void DeriveKey_IsDeterministic()
    {
        var key1 = RestoreEngine.DeriveKey("motdepasse");
        var key2 = RestoreEngine.DeriveKey("motdepasse");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_ProducesCorrectLength()
    {
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

    // ── DeriveKeyV2 (PBKDF2-SHA256) ─────────────────────────────────────────────

    [Fact]
    public void DeriveKeyV2_IsDeterministicWithSameSalt()
    {
        var salt = RestoreEngine.GenerateSalt();
        var key1 = RestoreEngine.DeriveKeyV2("motdepasse", salt);
        var key2 = RestoreEngine.DeriveKeyV2("motdepasse", salt);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKeyV2_ProducesCorrectLength()
    {
        var salt = RestoreEngine.GenerateSalt();
        var key = RestoreEngine.DeriveKeyV2("test", salt);
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void DeriveKeyV2_DiffersWithDifferentSalts()
    {
        var salt1 = RestoreEngine.GenerateSalt();
        var salt2 = RestoreEngine.GenerateSalt();
        var key1 = RestoreEngine.DeriveKeyV2("same-password", salt1);
        var key2 = RestoreEngine.DeriveKeyV2("same-password", salt2);
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKeyV2_DiffersFromDeriveKey()
    {
        var salt = RestoreEngine.GenerateSalt();
        var keyLegacy = RestoreEngine.DeriveKey("password");
        var keyV2 = RestoreEngine.DeriveKeyV2("password", salt);
        Assert.NotEqual(keyLegacy, keyV2);
    }

    [Fact]
    public void GenerateSalt_Produces32Bytes()
    {
        var salt = RestoreEngine.GenerateSalt();
        Assert.Equal(32, salt.Length);
    }

    [Fact]
    public void GenerateSalt_IsRandom()
    {
        var salt1 = RestoreEngine.GenerateSalt();
        var salt2 = RestoreEngine.GenerateSalt();
        Assert.NotEqual(salt1, salt2);
    }

    // ── Format v2 : CopyAndEncryptFileAsync ─────────────────────────────────────

    [Fact]
    public async Task EncryptedFile_V2Format_HasCorrectHeader()
    {
        var sourceContent = "Hello WinBack v2!"u8.ToArray();
        var sourceFile = Path.GetTempFileName();
        var encryptedFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllBytesAsync(sourceFile, sourceContent);

            var key = RestoreEngine.DeriveKey("password");
            await InvokeCopyAndEncryptFileAsync(sourceFile, encryptedFile, key, CancellationToken.None);

            var encrypted = await File.ReadAllBytesAsync(encryptedFile);

            // Header v2 : "WB02" (4) + IV (16) + ciphertext + HMAC (32)
            Assert.True(encrypted.Length >= 4 + 16 + 16 + 32, "Le fichier chiffré doit avoir l'en-tête v2 complet");
            Assert.Equal((byte)'W', encrypted[0]);
            Assert.Equal((byte)'B', encrypted[1]);
            Assert.Equal((byte)'0', encrypted[2]);
            Assert.Equal((byte)'2', encrypted[3]);
        }
        finally
        {
            File.Delete(sourceFile);
            File.Delete(encryptedFile);
        }
    }

    [Fact]
    public async Task RestoreEngine_DecryptsV2Correctly_Roundtrip()
    {
        var original = "Fichier WinBack v2 restauré"u8.ToArray();
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

    [Fact]
    public async Task RestoreEngine_DetectsTamperedFile()
    {
        var original = "Données sensibles"u8.ToArray();
        var sourceDir = Path.Combine(Path.GetTempPath(), "WB_Tamper_" + Guid.NewGuid());
        var destDir   = Path.Combine(Path.GetTempPath(), "WB_TmpDst_" + Guid.NewGuid());

        try
        {
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(destDir);

            var encryptedFile = Path.Combine(sourceDir, "tampered.txt");
            var rawFile = Path.GetTempFileName();
            await File.WriteAllBytesAsync(rawFile, original);

            var key = RestoreEngine.DeriveKey("password");
            await InvokeCopyAndEncryptFileAsync(rawFile, encryptedFile, key, CancellationToken.None);
            File.Delete(rawFile);

            // Altérer un octet du ciphertext (après magic + IV = offset 20+)
            var bytes = await File.ReadAllBytesAsync(encryptedFile);
            bytes[25] ^= 0xFF; // Flipper un octet dans le ciphertext
            await File.WriteAllBytesAsync(encryptedFile, bytes);

            var engine = new RestoreEngine(Microsoft.Extensions.Logging.Abstractions.NullLogger<RestoreEngine>.Instance);
            var result = await engine.RestoreAsync(
                new RestoreEngine.RestoreOptions(
                    SourceFolder: sourceDir,
                    DestinationFolder: destDir,
                    IsEncrypted: true,
                    DecryptionKey: key),
                ct: CancellationToken.None);

            // Le fichier altéré doit être détecté (HMAC invalide)
            Assert.Equal(1, result.Errored);
            Assert.Contains("HMAC", result.Errors[0]);
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
