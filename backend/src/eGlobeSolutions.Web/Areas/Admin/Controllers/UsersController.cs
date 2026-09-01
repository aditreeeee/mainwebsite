using eGlobeSolutions.Infrastructure.Identity;
using eGlobeSolutions.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

/// <summary>Admin user & role management. Only SuperAdmins may create/edit accounts or change roles.</summary>
[Area("Admin")]
[Route("admin/users")]
[Authorize(Policy = "SuperAdminOnly")]
public class UsersController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public UsersController(UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var users = _userManager.Users.OrderBy(u => u.Email).ToList();
        var items = new List<UserListItem>();
        foreach (var u in users)
        {
            items.Add(new UserListItem
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? string.Empty,
                IsActive = u.IsActive,
                LastLoginAtUtc = u.LastLoginAtUtc,
                Roles = (await _userManager.GetRolesAsync(u)).ToList()
            });
        }
        return View(items);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        return View(new CreateUserModel { AvailableRoles = await GetRoleNamesAsync() });
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserModel model)
    {
        model.AvailableRoles = await GetRoleNamesAsync();
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            FullName = model.FullName,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, model.Role);
        TempData["Success"] = $"Admin account created for {model.Email}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();
        var roles = await _userManager.GetRolesAsync(user);

        return View(new EditUserModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            IsActive = user.IsActive,
            Role = roles.FirstOrDefault() ?? string.Empty,
            AvailableRoles = await GetRoleNamesAsync()
        });
    }

    [HttpPost("{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, EditUserModel model)
    {
        model.AvailableRoles = await GetRoleNamesAsync();
        if (id != model.Id) return BadRequest();

        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        if (!ModelState.IsValid)
        {
            model.Email = user.Email ?? string.Empty;
            return View(model);
        }

        user.FullName = model.FullName;
        user.IsActive = model.IsActive;
        await _userManager.UpdateAsync(user);

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (!currentRoles.Contains(model.Role))
        {
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, model.Role);
        }

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetResult = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!resetResult.Succeeded)
            {
                foreach (var e in resetResult.Errors) ModelState.AddModelError(string.Empty, e.Description);
                model.Email = user.Email ?? string.Empty;
                return View(model);
            }
        }

        TempData["Success"] = "User updated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<string>> GetRoleNamesAsync() =>
        await Task.FromResult(_roleManager.Roles.Select(r => r.Name!).OrderBy(r => r).ToList());
}
