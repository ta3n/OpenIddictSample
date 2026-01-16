using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddictSample2.Data;
using OpenIddictSample2.Middleware;
using OpenIddictSample2.Services;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configure HttpContextAccessor for tenant service
builder.Services.AddHttpContextAccessor();

// Configure HttpClient for BFF
builder.Services.AddHttpClient();

// Configure Database
builder.Services.AddDbContext<ApplicationDbContext>(
    options =>
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
        options.UseOpenIddict();
    }
);

// Configure Redis for distributed caching (token storage)
builder.Services.AddStackExchangeRedisCache(
    options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("Redis");
        options.InstanceName = "OpenIddictSample_";
    }
);

// Register custom services
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<ITokenStorageService, TokenStorageService>();
builder.Services.AddSingleton<IKeyRotationService, KeyRotationService>();
builder.Services.AddScoped<IBffSessionService, BffSessionService>();

// Configure Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(
        options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
        }
    );

// Configure CORS for BFF (allow frontend SPA)
builder.Services.AddCors(
    options =>
    {
        options.AddPolicy(
            "BffPolicy",
            policy =>
            {
                policy.WithOrigins(
                        "http://localhost:3000", // React
                        "http://localhost:4200", // Angular
                        "http://localhost:8080", // Vue
                        "http://localhost:5173" // Vite
                    )
                    .AllowCredentials()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            }
        );
    }
);

// Configure Anti-forgery for BFF
builder.Services.AddAntiforgery(
    options =>
    {
        options.HeaderName = "X-CSRF-TOKEN";
        options.Cookie.Name = "CSRF-TOKEN";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    }
);

// Configure OpenIddict
builder.Services.AddOpenIddict()
    // Register Entity Framework Core stores
    .AddCore(
        options =>
        {
            options.UseEntityFrameworkCore()
                .UseDbContext<ApplicationDbContext>();
        }
    )

    // Register ASP.NET Core server
    .AddServer(
        options =>
        {
            // Enable the authorization, token, revocation endpoints
            options.SetAuthorizationEndpointUris("/connect/authorize")
                .SetTokenEndpointUris("/connect/token")
                .SetRevocationEndpointUris("/connect/revoke")
                .SetEndSessionEndpointUris("/connect/logout")
                .SetUserInfoEndpointUris("/connect/userinfo");

            // Enable authorization code flow
            options.AllowAuthorizationCodeFlow()
                .AllowRefreshTokenFlow()
                .AllowClientCredentialsFlow();

            // Note: Refresh token rotation is handled manually in AuthorizationController
            // Configure token reuse prevention
            options.DisableTokenStorage(); // We use Redis for token storage

            // Register signing and encryption credentials
            // Note: In production, use persistent keys from Key Rollover Service
            options.AddDevelopmentEncryptionCertificate()
                .AddDevelopmentSigningCertificate();

            // Register ASP.NET Core host
            options.UseAspNetCore()
                .EnableAuthorizationEndpointPassthrough()
                .EnableTokenEndpointPassthrough()
                .EnableEndSessionEndpointPassthrough()
                .EnableStatusCodePagesIntegration();

            // Configure token lifetimes
            options.SetAccessTokenLifetime(TimeSpan.FromMinutes(30));
            options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));
        }
    )

    // Register ASP.NET Core validation handler
    .AddValidation(
        options =>
        {
            options.UseLocalServer();
            options.UseAspNetCore();
        }
    );

var app = builder.Build();

// Initialize database and seed data
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
    var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

    // Ensure database is created
    await context.Database.EnsureCreatedAsync();

    // Seed default tenant
    if (!await context.Tenants.AnyAsync())
    {
        context.Tenants.Add(
            new OpenIddictSample2.Models.Tenant
            {
                Id = "tenant1",
                Name = "Default Tenant",
                Domain = "localhost",
                IsActive = true
            }
        );
        await context.SaveChangesAsync();
    }

    // Seed test user
    if (!await context.Users.AnyAsync())
    {
        // Create test user with password "password123"
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var passwordBytes = System.Text.Encoding.UTF8.GetBytes("password123");
        var hashBytes = sha256.ComputeHash(passwordBytes);
        var passwordHash = Convert.ToBase64String(hashBytes);

        context.Users.Add(
            new OpenIddictSample2.Models.ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                Username = "testuser",
                Email = "testuser@example.com",
                PasswordHash = passwordHash,
                TenantId = "tenant1",
                CreatedAt = DateTime.UtcNow
            }
        );
        await context.SaveChangesAsync();
    }

    // Seed OAuth client application
    if (await applicationManager.FindByClientIdAsync("postman-client") == null)
    {
        await applicationManager.CreateAsync(
            new OpenIddictApplicationDescriptor
            {
                ClientId = "postman-client",
                ClientSecret = "postman-secret",
                DisplayName = "Postman Test Client",
                RedirectUris = { new Uri("https://oauth.pstmn.io/v1/callback") },
                PostLogoutRedirectUris = { new Uri("https://oauth.pstmn.io/v1/callback") },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.Revocation,
                    Permissions.Endpoints.EndSession,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.ResponseTypes.Code,
                    "scp:openid", // OpenID Connect scope
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile,
                    Permissions.Scopes.Roles,
                    "scp:api" // Custom API scope
                }
            }
        );
    }

    // Seed scopes
    if (await scopeManager.FindByNameAsync("openid") == null)
    {
        await scopeManager.CreateAsync(
            new OpenIddictScopeDescriptor
            {
                Name = "openid",
                DisplayName = "OpenID",
                Description = "OpenID Connect scope"
            }
        );
    }

    if (await scopeManager.FindByNameAsync("profile") == null)
    {
        await scopeManager.CreateAsync(
            new OpenIddictScopeDescriptor
            {
                Name = "profile",
                DisplayName = "Profile",
                Description = "User profile information (name, username, etc.)"
            }
        );
    }

    if (await scopeManager.FindByNameAsync("email") == null)
    {
        await scopeManager.CreateAsync(
            new OpenIddictScopeDescriptor
            {
                Name = "email",
                DisplayName = "Email",
                Description = "User email address"
            }
        );
    }

    if (await scopeManager.FindByNameAsync("api") == null)
    {
        await scopeManager.CreateAsync(
            new OpenIddictScopeDescriptor
            {
                Name = "api",
                DisplayName = "API Access",
                Description = "Access to backend API resources",
                Resources = { "resource_server" }
            }
        );
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Enable CORS for BFF
app.UseCors("BffPolicy");

// Add BFF security headers
app.UseBffSecurityHeaders();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    "default",
    "{controller=Home}/{action=Index}/{id?}"
);

await app.RunAsync();