using WinBack.Core.Models;
using Xunit;

namespace WinBack.Tests;

public class BackupRunTests
{
    // ── Duration ───────────────────────────────────────────────────────────

    [Fact]
    public void Duration_WhenFinishedAtIsNull_ReturnsNull()
    {
        var run = new BackupRun { StartedAt = DateTime.UtcNow, FinishedAt = null };
        Assert.Null(run.Duration);
    }

    [Fact]
    public void Duration_WhenFinishedAtIsSet_ReturnsCorrectDuration()
    {
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var finish = start.AddMinutes(5).AddSeconds(30);
        var run = new BackupRun { StartedAt = start, FinishedAt = finish };

        Assert.Equal(TimeSpan.FromSeconds(330), run.Duration);
    }

    [Fact]
    public void Duration_ZeroLength_ReturnsZero()
    {
        var now = DateTime.UtcNow;
        var run = new BackupRun { StartedAt = now, FinishedAt = now };
        Assert.Equal(TimeSpan.Zero, run.Duration);
    }

    // ── TotalFiles ─────────────────────────────────────────────────────────

    [Fact]
    public void TotalFiles_SumsAllThreeCounters()
    {
        var run = new BackupRun { FilesAdded = 3, FilesModified = 5, FilesDeleted = 2 };
        Assert.Equal(10, run.TotalFiles);
    }

    [Fact]
    public void TotalFiles_AllZero_ReturnsZero()
    {
        var run = new BackupRun();
        Assert.Equal(0, run.TotalFiles);
    }

    [Fact]
    public void TotalFiles_OnlyAdded_ReturnsAddedCount()
    {
        var run = new BackupRun { FilesAdded = 7 };
        Assert.Equal(7, run.TotalFiles);
    }

    [Fact]
    public void TotalFiles_DoesNotIncludeErrored()
    {
        var run = new BackupRun { FilesAdded = 2, FilesModified = 1, FilesDeleted = 1, FilesErrored = 10 };
        Assert.Equal(4, run.TotalFiles);
    }

    // ── Status initial ─────────────────────────────────────────────────────

    [Fact]
    public void NewRun_DefaultStatus_IsRunning()
    {
        var run = new BackupRun();
        Assert.Equal(BackupRunStatus.Running, run.Status);
    }

    // ── Statut Interrupted ─────────────────────────────────────────────────

    [Fact]
    public void Status_CanBeSetToInterrupted()
    {
        var run = new BackupRun { Status = BackupRunStatus.Interrupted };
        Assert.Equal(BackupRunStatus.Interrupted, run.Status);
    }
}
