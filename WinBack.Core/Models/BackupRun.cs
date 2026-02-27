namespace WinBack.Core.Models;

public enum BackupRunStatus
{
    Running,
    Success,
    PartialSuccess,
    Cancelled,
    Error
}

public class BackupRun
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public BackupProfile Profile { get; set; } = null!;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public int FilesAdded { get; set; }
    public int FilesModified { get; set; }
    public int FilesDeleted { get; set; }
    public int FilesErrored { get; set; }

    public long BytesTransferred { get; set; }

    public BackupRunStatus Status { get; set; } = BackupRunStatus.Running;
    public string? ErrorMessage { get; set; }

    /// <summary>Si vrai, aucun fichier n'a été écrit (simulation).</summary>
    public bool IsDryRun { get; set; }

    // Propriétés calculées (non persistées)
    public TimeSpan? Duration => FinishedAt.HasValue ? FinishedAt.Value - StartedAt : null;
    public int TotalFiles => FilesAdded + FilesModified + FilesDeleted;

    public List<BackupRunEntry> Entries { get; set; } = [];
}

public enum EntryAction { Added, Modified, Deleted, Error }

public class BackupRunEntry
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public BackupRun Run { get; set; } = null!;

    public EntryAction Action { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string? ErrorDetail { get; set; }
}
