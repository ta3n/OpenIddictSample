# OpenIddict Implementation - Technical Documentation

## Overview

This implementation provides a complete OAuth 2.0 / OpenID Connect authorization server using OpenIddict 5.8.0 on .NET 8.0.

## Architecture

### Components

```
┌─────────────────────────────────────────────────────────────┐
│                      Authorization Server                    │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌──────────────────┐  ┌──────────────────┐                │
│  │  Authorization   │  │   Token          │                │
│  │  Controller      │  │   Endpoint       │                │
│  └──────────────────┘  └──────────────────┘                │
│           │                      │                           │
│           ▼                      ▼                           │
│  ┌───────────────────────────────────────┐                 │
│  │      OpenIddict Server Core           │                 │
│  └───────────────────────────────────────┘                 │
│           │                                                  │
│           ▼                                                  │
│  ┌──────────────────┐  ┌──────────────────┐                │
│  │   Multi-Tenant   │  │  Redis Token     │                │
│  │   Middleware     │  │  Service         │                │
│  └──────────────────┘  └──────────────────┘                │
│           │                      │                           │
│           ▼                      ▼                           │
│  ┌──────────────────┐  ┌──────────────────┐                │
│  │  EF Core DB      │  │  Redis Cache     │                │
│  │  (SQLite)        │  │                  │                │
│  └──────────────────┘  └──────────────────┘                │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

## Features Implementation

### 1. Authorization Code Flow

**Implementation**: `AuthorizationController.Authorize()`

The authorization code flow is implemented following OAuth 2.0 RFC 6749:

1. Client redirects user to `/connect/authorize`
2. User authentication (simplified for demo)
3. Authorization code generated and returned
4. Client exchanges code for tokens at `/connect/token`

**Key Code**:
```csharp
[HttpGet("~/connect/authorize")]
[HttpPost("~/connect/authorize")]
public async Task<IActionResult> Authorize()
{
    // Authenticate user
    // Create authorization
    // Return authorization code
    return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
```

### 2. Refresh Token Rotation

**Implementation**: `RefreshToken` model + `AuthorizationController.Exchange()`

Refresh token rotation is a security best practice that ensures each refresh token can only be used once:

**Process**:
1. Client presents refresh token
2. Server validates token
3. Old token is marked as revoked
4. New access token + refresh token issued
5. Old token stored with reference to new token

**Database Schema**:
```sql
CREATE TABLE RefreshTokens (
    Id TEXT PRIMARY KEY,
    Token TEXT UNIQUE NOT NULL,
    UserId TEXT NOT NULL,
    TenantId TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    IsRevoked INTEGER NOT NULL,
    ReplacedByToken TEXT NULL
);
```

**Security Features**:
- One-time use tokens
- Revocation tracking
- Token chaining for audit trail
- Expiration management

### 3. Logout/Revoke

**Logout**: `AuthorizationController.Logout()`
- Revokes all tokens for the user
- Signs out from authentication

**Revoke**: `AuthorizationController.Revoke()`
- Revokes specific token (access or refresh)
- Removes from Redis cache
- Marks refresh tokens as revoked in database

**Endpoints**:
- `POST /connect/logout` - Revokes all user tokens
- `POST /connect/revoke` - Revokes specific token

### 4. Redis-backed Tokens

**Implementation**: `RedisTokenService`

Access tokens are cached in Redis for fast validation:

**Benefits**:
- High-performance token validation
- Distributed cache support
- Scalability across multiple servers
- TTL-based automatic expiration

**Configuration**:
```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

**Usage**:
```csharp
await _redisTokenService.StoreTokenAsync(
    $"access_token:{accessToken}",
    tokenData,
    TimeSpan.FromHours(1)
);
```

### 5. Multi-Tenant Isolation

**Implementation**: `TenantMiddleware`

Multi-tenancy is implemented using request-scoped tenant identification:

**Tenant Identification Methods**:
1. HTTP Header: `X-Tenant-ID`
2. Query Parameter: `?tenant=<id>`
3. Default: "default"

**Isolation Levels**:
- Users are isolated by tenant
- Tokens are scoped to tenant
- Refresh tokens are tenant-specific
- Authorization grants are tenant-aware

**Database Strategy**:
- Shared database with tenant discriminator
- Composite unique indexes on (TenantId, Username)
- Query filters applied automatically

### 6. Key Rollover & JWKS Rotation

**Implementation**: `KeyRolloverService`

A background service manages cryptographic key lifecycle:

**Features**:
- Automatic key generation
- JWKS endpoint (`/.well-known/jwks`)
- Multiple active keys for transition periods
- 90-day rotation interval (configurable)

**Key Management**:
- Development: Uses OpenIddict development certificates
- Production: Should use Azure Key Vault, HSM, or certificate store

**JWKS Endpoint**:
```json
{
  "keys": [
    {
      "kid": "...",
      "use": "sig",
      "kty": "RSA",
      "alg": "RS256",
      "e": "AQAB",
      "n": "..."
    }
  ]
}
```

## Database Schema

### Users Table
```sql
CREATE TABLE Users (
    Id TEXT PRIMARY KEY,
    Username TEXT NOT NULL,
    PasswordHash TEXT NOT NULL,
    TenantId TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UNIQUE(TenantId, Username)
);
```

### RefreshTokens Table
```sql
CREATE TABLE RefreshTokens (
    Id TEXT PRIMARY KEY,
    Token TEXT UNIQUE NOT NULL,
    UserId TEXT NOT NULL,
    TenantId TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    IsRevoked INTEGER NOT NULL,
    ReplacedByToken TEXT NULL,
    INDEX(TenantId, UserId)
);
```

### OpenIddict Tables
- OpenIddictApplications
- OpenIddictAuthorizations
- OpenIddictScopes
- OpenIddictTokens

## Security Considerations

### Token Lifetimes
- **Access Token**: 1 hour
- **Refresh Token**: 30 days
- **Authorization Code**: 5 minutes (OpenIddict default)

### Token Revocation
- Refresh tokens can be revoked individually
- All user tokens can be revoked on logout
- Revoked tokens are permanently marked in database

### HTTPS Requirements
- Development: HTTP allowed for testing
- Production: HTTPS required (recommended)

### Password Storage
- Passwords hashed using PBKDF2
- 100,000 iterations
- HMACSHA256 algorithm
- Random salt per password

## API Endpoints

### Discovery Endpoints
- `GET /.well-known/openid-configuration` - OpenID Connect discovery
- `GET /.well-known/jwks` - JSON Web Key Set

### OAuth 2.0 Endpoints
- `GET/POST /connect/authorize` - Authorization endpoint
- `POST /connect/token` - Token endpoint
- `POST /connect/revoke` - Revocation endpoint
- `GET/POST /connect/logout` - Logout endpoint

### Application Endpoints
- `GET /health` - Health check
- `GET /api/protected` - Protected resource (example)

## Configuration

### appsettings.json
```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "OpenIddict": {
    "AccessTokenLifetime": "01:00:00",
    "RefreshTokenLifetime": "30.00:00:00",
    "KeyRolloverInterval": "90.00:00:00"
  }
}
```

## Testing

### Using the Test Script
```bash
chmod +x test-features.sh
./test-features.sh
```

### Manual Testing with curl

**Get Discovery Document**:
```bash
curl http://localhost:5002/.well-known/openid-configuration
```

**Request Token (Password Grant)**:
```bash
curl -X POST http://localhost:5002/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -H "X-Tenant-ID: default" \
  -d "grant_type=password&username=testuser&password=password&client_id=test-client&client_secret=test-secret&scope=api openid"
```

**Refresh Token**:
```bash
curl -X POST http://localhost:5002/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -H "X-Tenant-ID: default" \
  -d "grant_type=refresh_token&refresh_token=<token>&client_id=test-client&client_secret=test-secret"
```

**Revoke Token**:
```bash
curl -X POST http://localhost:5002/connect/revoke \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -H "X-Tenant-ID: default" \
  -d "token=<token>&client_id=test-client&client_secret=test-secret"
```

## Deployment

### Production Checklist
- [ ] Replace development certificates with production certificates
- [ ] Configure production Redis cluster
- [ ] Use production database (PostgreSQL/SQL Server recommended)
- [ ] Enable HTTPS enforcement
- [ ] Configure CORS policies
- [ ] Set up key management (Azure Key Vault/HSM)
- [ ] Implement logging and monitoring
- [ ] Configure rate limiting
- [ ] Set up load balancing
- [ ] Enable database backups

### Environment Variables
```bash
ConnectionStrings__Redis=your-redis-connection
ASPNETCORE_ENVIRONMENT=Production
```

## Performance Optimization

### Redis Configuration
- Use connection multiplexer pooling
- Configure appropriate timeouts
- Use Redis Cluster for high availability

### Database Optimization
- Add indexes on frequently queried columns
- Use connection pooling
- Consider read replicas for scale

### Token Validation
- Cache validation results in Redis
- Use distributed cache for multi-server deployments

## Troubleshooting

### Common Issues

**HTTPS Required Error**:
- Ensure `DisableTransportSecurityRequirement()` is called in development
- Use HTTPS in production

**Redis Connection Failed**:
- Check Redis server is running
- Verify connection string
- Check firewall rules

**Token Validation Failed**:
- Verify token hasn't expired
- Check token hasn't been revoked
- Ensure correct tenant context

## References

- [OpenIddict Documentation](https://documentation.openiddict.com/)
- [OAuth 2.0 RFC 6749](https://tools.ietf.org/html/rfc6749)
- [OpenID Connect Core](https://openid.net/specs/openid-connect-core-1_0.html)
- [Token Revocation RFC 7009](https://tools.ietf.org/html/rfc7009)
