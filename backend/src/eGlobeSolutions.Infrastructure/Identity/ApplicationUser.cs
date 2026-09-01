using Microsoft.AspNetCore.Identity;

namespace eGlobeSolutions.Infrastructure.Identity;

/// <summary>
/// Admin-panel user. There is no public/customer account system, only
/// internal staff who manage content and enquiries.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }
}
