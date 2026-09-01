namespace eGlobeSolutions.Domain.Entities;

/// <summary>
/// Append-only audit trail for admin actions. Written by the admin activity
/// logging filter/service, never edited by users.
/// </summary>
public class ActivityLog
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public string? UserId { get; set; }
    public string? UserDisplayName { get; set; }

    /// <summary>e.g. "Enquiry.StatusChanged", "Enquiry.Viewed", "Admin.Login".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The entity type affected, e.g. "Enquiry".</summary>
    public string? EntityType { get; set; }

    /// <summary>The affected entity's Id, stored as string to stay generic across entity types.</summary>
    public string? EntityId { get; set; }

    /// <summary>Short human-readable summary shown in the admin activity feed.</summary>
    public string? Summary { get; set; }

    public string? IpAddress { get; set; }
}
