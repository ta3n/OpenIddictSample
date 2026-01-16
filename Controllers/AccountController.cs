using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using OpenIddictSample.Data;
using OpenIddictSample.Entities;
using OpenIddictSample.Models;
using OpenIddictSample.Services;

namespace OpenIddictSample.Controllers;

/// <summary>
/// Account management controller
/// Handles login, registration, and user management
/// </summary>
[Route("account")]
public class AccountController(
    ApplicationDbContext context,
    ITenantService tenantService
) : Controller
{
    [HttpGet]
    public IActionResult Login(
        string? returnUrl = null
    )
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [IgnoreAntiforgeryToken] // Allow testing via Postman
    public async Task<IActionResult> Login(
        string username,
        string password,
        string? returnUrl = null
    )
    {
        var tenantId = tenantService.GetCurrentTenantId();
        if (string.IsNullOrEmpty(tenantId))
        {
            ModelState.AddModelError("", "Tenant ID is required");
            return View();
        }

        // Find user by username and tenant
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.TenantId == tenantId);

        if (user == null || !VerifyPassword(password, user.PasswordHash))
        {
            ModelState.AddModelError("", "Invalid username or password");
            return View();
        }

        // Create claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new("tenant_id", user.TenantId)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            claimsPrincipal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
            }
        );

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        // Validate and redirect to returnUrl if provided and local
        if (!string.IsNullOrEmpty(returnUrl) && (returnUrl.StartsWith('/') || returnUrl.StartsWith("~/")))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Register(
        string username,
        string email,
        string password,
        string tenantId
    )
    {
        // Validate tenant
        if (!await tenantService.ValidateTenantAsync(tenantId))
        {
            ModelState.AddModelError("", "Invalid tenant");
            return View();
        }

        // Check if username already exists
        if (await context.Users.AnyAsync(u => u.Username == username))
        {
            ModelState.AddModelError("", "Username already exists");
            return View();
        }

        // Create new user
        var user = new ApplicationUser
        {
            Username = username,
            Email = email,
            PasswordHash = HashPassword(password),
            TenantId = tenantId
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    private static string HashPassword(
        string password
    )
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static bool VerifyPassword(
        string password,
        string hash
    )
    {
        return HashPassword(password) == hash;
    }
}
