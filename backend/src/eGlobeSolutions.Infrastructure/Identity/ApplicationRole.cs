using Microsoft.AspNetCore.Identity;

namespace eGlobeSolutions.Infrastructure.Identity;

public class ApplicationRole : IdentityRole
{
    public string? Description { get; set; }

    public static class Names
    {
        /// <summary>Full access: users, roles, settings, all content modules.</summary>
        public const string SuperAdmin = "SuperAdmin";

        /// <summary>Can manage site content and pricing but not users/roles/settings.</summary>
        public const string ContentEditor = "ContentEditor";

        /// <summary>Can view and action enquiries (Contact Sales / Reseller) only.</summary>
        public const string SalesAgent = "SalesAgent";
    }
}
