using WinBack.Core.Models;
using WinBack.Core.Services;
using Xunit;

namespace WinBack.Tests;

public class DiffCalculatorTests : IDisposable
{
    private readonly string _sourceDir;
    private readonly string _testRoot;
    private readonly DiffCalculator _differ = new();
    private readonly BackupPair _pair = new();

    public DiffCalculatorTests()
    {
        _testRoot  = Path.Combine(Path.GetTempPath(), "WinBackTests_" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_testRoot, "source");
        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch { /* best-effort */ }
    }

    private static FileSnapshot MakeSnapshot(string relativePath, long size, DateTime lastModified) =>
        new() { ProfileId = 1, PairId = 1, RelativePath = relativePath, Size = size, LastModified = lastModified };

    private void WriteFile(string relativePath, string content = "content")
    {
        var fullPath = Path.Combine(_sourceDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    // ── Source vide → tout supprimé ────────────────────────────────────────

    [Fact]
    public void Compute_SourceMissing_AllDeleted()
    {
        var snapshots = new List<FileSnapshot>
        {
            MakeSnapshot("a.txt", 10, DateTime.UtcNow),
            MakeSnapshot("b.txt", 20, DateTime.UtcNow),
        };

        var diff = _differ.Compute("C:\\NonExistentPath_" + Guid.NewGuid(), snapshots, _pair);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Modified);
        Assert.Equal(2, diff.Deleted.Count);
    }

    [Fact]
    public void Compute_EmptySource_NoSnapshots_NoChanges()
    {
        var diff = _differ.Compute(_sourceDir, [], _pair);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Modified);
        Assert.Empty(diff.Deleted);
    }

    // ── Nouveaux fichiers → Added ──────────────────────────────────────────

    [Fact]
    public void Compute_NewFiles_MarkedAsAdded()
    {
        WriteFile("new1.txt");
        WriteFile("sub/new2.txt");

        var diff = _differ.Compute(_sourceDir, [], _pair);

        Assert.Equal(2, diff.Added.Count);
        Assert.Empty(diff.Modified);
        Assert.Empty(diff.Deleted);
    }

    // ── Fichiers modifiés → Modified ──────────────────────────────────────

    [Fact]
    public void Compute_FileSizeChanged_MarkedAsModified()
    {
        WriteFile("file.txt", "short");
        var snapshots = new List<FileSnapshot>
        {
            MakeSnapshot("file.txt", 999, File.GetLastWriteTimeUtc(Path.Combine(_sourceDir, "file.txt")))
        };

        var diff = _differ.Compute(_sourceDir, snapshots, _pair);

        Assert.Empty(diff.Added);
        Assert.Contains("file.txt", diff.Modified);
        Assert.Empty(diff.Deleted);
    }

    [Fact]
    public void Compute_FileDateChanged_MarkedAsModified()
    {
        WriteFile("file.txt", "hello");
        var fullPath = Path.Combine(_sourceDir, "file.txt");
        var oldDate = DateTime.UtcNow.AddDays(-1);
        var snapshots = new List<FileSnapshot>
        {
            MakeSnapshot("file.txt", new FileInfo(fullPath).Length, oldDate)
        };

        var diff = _differ.Compute(_sourceDir, snapshots, _pair);

        Assert.Empty(diff.Added);
        Assert.Contains("file.txt", diff.Modified);
        Assert.Empty(diff.Deleted);
    }

    // ── Fichiers inchangés → pas dans Added ni Modified ───────────────────

    [Fact]
    public void Compute_UnchangedFile_NotInDiff()
    {
        WriteFile("file.txt", "hello");
        var fullPath = Path.Combine(_sourceDir, "file.txt");
        var info = new FileInfo(fullPath);
        var snapshots = new List<FileSnapshot>
        {
            MakeSnapshot("file.txt", info.Length, info.LastWriteTimeUtc)
        };

        var diff = _differ.Compute(_sourceDir, snapshots, _pair);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Modified);
        Assert.Empty(diff.Deleted);
    }

    // ── Fichiers supprimés → Deleted ──────────────────────────────────────

    [Fact]
    public void Compute_FileRemovedFromSource_MarkedAsDeleted()
    {
        // Pas de fichier dans le répertoire source, mais snapshot existe
        var snapshots = new List<FileSnapshot>
        {
            MakeSnapshot("ghost.txt", 100, DateTime.UtcNow)
        };

        var diff = _differ.Compute(_sourceDir, snapshots, _pair);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Modified);
        Assert.Contains("ghost.txt", diff.Deleted);
    }

    // ── Exclusions : fichier exclu ignoré ─────────────────────────────────

    [Fact]
    public void Compute_ExcludedFile_NotInDiff()
    {
        WriteFile("notes.tmp");
        WriteFile("notes.txt");

        var pairWithExclusion = new BackupPair();
        pairWithExclusion.ExcludePatterns = ["*.tmp"];

        var diff = _differ.Compute(_sourceDir, [], pairWithExclusion);

        Assert.Single(diff.Added);
        Assert.Contains("notes.txt", diff.Added);
        Assert.DoesNotContain("notes.tmp", diff.Added);
    }

    [Fact]
    public void Compute_ExcludedDirectory_ChildrenIgnored()
    {
        WriteFile(@"node_modules\lodash\index.js");
        WriteFile("index.ts");

        var pairWithExclusion = new BackupPair();
        pairWithExclusion.ExcludePatterns = ["node_modules/**"];

        var diff = _differ.Compute(_sourceDir, [], pairWithExclusion);

        Assert.Single(diff.Added);
        Assert.Contains("index.ts", diff.Added);
    }

    // ── Combinaison added + modified + deleted ─────────────────────────────

    [Fact]
    public void Compute_MixedChanges_CorrectClassification()
    {
        WriteFile("added.txt");
        WriteFile("modified.txt", "new content");

        var fullPath = Path.Combine(_sourceDir, "modified.txt");
        var snapshots = new List<FileSnapshot>
        {
            MakeSnapshot("modified.txt", 999, DateTime.UtcNow.AddDays(-1)),
            MakeSnapshot("deleted.txt", 50, DateTime.UtcNow),
        };

        var diff = _differ.Compute(_sourceDir, snapshots, _pair);

        Assert.Contains("added.txt", diff.Added);
        Assert.Contains("modified.txt", diff.Modified);
        Assert.Contains("deleted.txt", diff.Deleted);
    }
}
