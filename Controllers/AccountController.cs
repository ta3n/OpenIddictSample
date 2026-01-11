using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddictSample2.Data;
using OpenIddictSample2.Models;
using OpenIddictSample2.Services;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace OpenIddictSample2.Controllers;

/// <summary>
/// Account management controller
/// Handles login, registration, and user management
/// </summary>
public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantService _tenantService;

    public AccountController(ApplicationDbContext context, ITenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        var tenantId = _tenantService.GetCurrentTenantId();
        if (string.IsNullOrEmpty(tenantId))
        {
            ModelState.AddModelError("", "Tenant ID is required");
            return View();
        }

        // Find user by username and tenant
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.TenantId == tenantId);

        if (user == null || !VerifyPassword(password, user.PasswordHash))
        {
            ModelState.AddModelError("", "Invalid username or password");
            return View();
        }

        // Create claims
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("tenant_id", user.TenantId)
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
            });

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
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
    public async Task<IActionResult> Register(string username, string email, string password, string tenantId)
    {
        // Validate tenant
        if (!await _tenantService.ValidateTenantAsync(tenantId))
        {
            ModelState.AddModelError("", "Invalid tenant");
            return View();
        }

        // Check if username already exists
        if (await _context.Users.AnyAsync(u => u.Username == username))
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

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
    }
}
