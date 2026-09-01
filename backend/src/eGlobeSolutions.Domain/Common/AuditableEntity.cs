namespace eGlobeSolutions.Domain.Common;

/// <summary>
/// Base type for entities that track creation/modification and support soft delete.
/// </summary>
public abstract class AuditableEntity
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Soft delete flag. Records are never hard-deleted from the CMS unless explicitly purged.</summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }
}
