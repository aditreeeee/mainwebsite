using System.ComponentModel.DataAnnotations;

namespace eGlobeSolutions.Web.Areas.Admin.Models;

public class UserListItem
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public List<string> Roles { get; set; } = new();
}

public class CreateUserModel
{
    [Required, StringLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 10)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;

    public List<string> AvailableRoles { get; set; } = new();
}

public class EditUserModel
{
    public string Id { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public string Role { get; set; } = string.Empty;
    public List<string> AvailableRoles { get; set; } = new();

    [StringLength(100, MinimumLength = 10)]
    public string? NewPassword { get; set; }
}
