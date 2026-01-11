using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddictSample.Data;
using OpenIddictSample.Middleware;
using OpenIddictSample.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// Configure Entity Framework with SQLite
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlite("Data Source=openiddict.db");
    options.UseOpenIddict();
});

// Configure Redis for token storage
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = ConfigurationOptions.Parse(redisConnection);
    configuration.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(configuration);
});

// Register services
builder.Services.AddSingleton<RedisTokenService>();
builder.Services.AddSingleton<PasswordHasher>();

// Configure OpenIddict
builder.Services.AddOpenIddict()
    // Register the OpenIddict core components
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
            .UseDbContext<ApplicationDbContext>();
    })
    // Register the OpenIddict server components
    .AddServer(options =>
    {
        // Enable the authorization, token, revocation and logout endpoints
        options.SetAuthorizationEndpointUris("/connect/authorize")
            .SetTokenEndpointUris("/connect/token")
            .SetRevocationEndpointUris("/connect/revoke")
            .SetLogoutEndpointUris("/connect/logout");

        // Enable the authorization code, refresh token and password flows
        options.AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow()
            .AllowPasswordFlow();

        // Register scopes
        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            "api");

        // Configure token lifetimes
        options.SetAccessTokenLifetime(TimeSpan.FromHours(1))
            .SetRefreshTokenLifetime(TimeSpan.FromDays(30));

        // Enable refresh token rotation
        options.SetRefreshTokenReuseLeeway(TimeSpan.Zero);

        // Register signing and encryption credentials
        options.AddDevelopmentEncryptionCertificate()
            .AddDevelopmentSigningCertificate();

        // Register ASP.NET Core host and enable token endpoint pass-through
        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableLogoutEndpointPassthrough()
            .EnableStatusCodePagesIntegration()
            .DisableTransportSecurityRequirement(); // Allow HTTP in development

        // Disable HTTPS requirement for development
        if (builder.Environment.IsDevelopment())
        {
            options.DisableAccessTokenEncryption();
        }
    })
    // Register the OpenIddict validation components
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

// Add authentication without setting default scheme
builder.Services.AddAuthentication();

// Add hosted services
builder.Services.AddHostedService<DatabaseInitializerWorker>();
builder.Services.AddHostedService<KeyRolloverService>();

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();

// Use tenant middleware for multi-tenant isolation
app.UseTenantMiddleware();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Sample protected endpoint
app.MapGet("/api/protected", () => new { message = "This is a protected resource" })
    .RequireAuthorization()
    .WithName("GetProtectedResource")
    .WithOpenApi();

// Health check endpoint
app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow })
    .WithName("HealthCheck")
    .WithOpenApi();

app.Run();
