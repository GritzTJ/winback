using WinBack.Core.Models;
using Xunit;

namespace WinBack.Tests;

public class BackupPairTests
{
    private static BackupPair MakePair(params string[] patterns)
    {
        var pair = new BackupPair();
        pair.ExcludePatterns = [.. patterns];
        return pair;
    }

    // ── IsExcluded : cas vides ──────────────────────────────────────────────

    [Fact]
    public void IsExcluded_NoPatterns_ReturnsFalse()
    {
        var pair = MakePair();
        Assert.False(pair.IsExcluded("file.tmp"));
    }

    // ── MatchesGlob via IsExcluded : extension ──────────────────────────────

    [Fact]
    public void IsExcluded_StarTmp_MatchesTmpFile()
    {
        var pair = MakePair("*.tmp");
        Assert.True(pair.IsExcluded("file.tmp"));
    }

    [Fact]
    public void IsExcluded_StarTmp_DoesNotMatchTmpxFile()
    {
        var pair = MakePair("*.tmp");
        Assert.False(pair.IsExcluded("file.tmpx"));
    }

    [Fact]
    public void IsExcluded_StarTmp_MatchesNestedTmpFile()
    {
        var pair = MakePair("*.tmp");
        Assert.True(pair.IsExcluded(@"subdir\file.tmp"));
    }

    // ── MatchesGlob : préfixe tilde ─────────────────────────────────────────

    [Fact]
    public void IsExcluded_TildePrefix_MatchesOfficeTemp()
    {
        var pair = MakePair("~$*");
        Assert.True(pair.IsExcluded("~$document.docx"));
    }

    [Fact]
    public void IsExcluded_TildePrefix_DoesNotMatchRegularFile()
    {
        var pair = MakePair("~$*");
        Assert.False(pair.IsExcluded("document.docx"));
    }

    // ── MatchesGlob : double étoile ─────────────────────────────────────────

    [Fact]
    public void IsExcluded_NodeModules_MatchesSubfolder()
    {
        var pair = MakePair("node_modules/**");
        Assert.True(pair.IsExcluded("node_modules/lodash/index.js"));
    }

    [Fact]
    public void IsExcluded_NodeModules_MatchesDirectChild()
    {
        var pair = MakePair("node_modules/**");
        Assert.True(pair.IsExcluded("node_modules/package.json"));
    }

    [Fact]
    public void IsExcluded_NodeModules_DoesNotMatchSiblingFolder()
    {
        var pair = MakePair("node_modules/**");
        Assert.False(pair.IsExcluded("other_modules/file.js"));
    }

    // ── MatchesGlob : séparateurs de chemin ────────────────────────────────
    //
    // Les tests ci-dessus comparaient un pattern en '/' à un chemin en '/', ce qui
    // ne se produit jamais en production : Path.GetRelativePath renvoie des '\' sous
    // Windows. Les cas réels sont donc mixtes.

    [Fact]
    public void IsExcluded_SlashPattern_MatchesBackslashPath()
    {
        var pair = MakePair("node_modules/**");
        Assert.True(pair.IsExcluded(@"node_modules\lodash\index.js"));
    }

    [Fact]
    public void IsExcluded_BackslashPattern_MatchesSlashPath()
    {
        var pair = MakePair(@"node_modules\**");
        Assert.True(pair.IsExcluded("node_modules/lodash/index.js"));
    }

    [Fact]
    public void IsExcluded_SlashPattern_DoesNotMatchSiblingWithBackslashPath()
    {
        var pair = MakePair("node_modules/**");
        Assert.False(pair.IsExcluded(@"other_modules\file.js"));
    }

    [Fact]
    public void IsExcluded_NestedSlashPattern_MatchesBackslashPath()
    {
        var pair = MakePair("src/obj/**");
        Assert.True(pair.IsExcluded(@"src\obj\Debug\app.dll"));
        Assert.False(pair.IsExcluded(@"src\bin\Debug\app.dll"));
    }

    // ── MatchesGlob : insensibilité à la casse ─────────────────────────────

    [Fact]
    public void IsExcluded_CaseInsensitive_MatchesUppercase()
    {
        var pair = MakePair("*.tmp");
        Assert.True(pair.IsExcluded("FILE.TMP"));
    }

    // ── MatchesGlob : point d'interrogation ────────────────────────────────

    [Fact]
    public void IsExcluded_QuestionMark_MatchesSingleChar()
    {
        var pair = MakePair("file?.txt");
        Assert.True(pair.IsExcluded("file1.txt"));
        Assert.True(pair.IsExcluded("fileA.txt"));
    }

    [Fact]
    public void IsExcluded_QuestionMark_DoesNotMatchTwoChars()
    {
        var pair = MakePair("file?.txt");
        Assert.False(pair.IsExcluded("file12.txt"));
    }

    // ── Plusieurs patterns ──────────────────────────────────────────────────

    [Fact]
    public void IsExcluded_MultiplePatterns_MatchesAny()
    {
        var pair = MakePair("*.tmp", "*.log", "~$*");
        Assert.True(pair.IsExcluded("debug.log"));
        Assert.True(pair.IsExcluded("~$report.xlsx"));
        Assert.False(pair.IsExcluded("report.xlsx"));
    }
}
