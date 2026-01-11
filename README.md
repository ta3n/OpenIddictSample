# OpenIddictSample

A complete implementation of OpenIddict OAuth 2.0 / OpenID Connect server with .NET 8.0 featuring:

## Features

- ✅ **Authorization Code Flow** - Secure authorization code grant type for web applications
- ✅ **Refresh Token Rotation** - Automatic rotation of refresh tokens for enhanced security
- ✅ **Logout/Revoke** - Token revocation and logout endpoints
- ✅ **Redis-backed tokens** - Token storage in Redis for scalability and performance
- ✅ **Multi-Tenant isolation** - Support for multiple tenants with data isolation
- ✅ **Key Rollover & JWKS rotation** - Automatic key management and JWKS endpoint

## Architecture

### Components

1. **Authorization Server** (`AuthorizationController`)
   - `/connect/authorize` - Authorization endpoint
   - `/connect/token` - Token endpoint (authorization code, refresh token, password grants)
   - `/connect/revoke` - Token revocation endpoint
   - `/connect/logout` - Logout endpoint

2. **Data Layer**
   - Entity Framework Core with SQLite
   - Multi-tenant user management
   - Refresh token tracking and rotation

3. **Redis Token Storage**
   - Access tokens cached in Redis
   - Configurable expiration
   - High-performance token validation

4. **Multi-Tenant Support**
   - Tenant identification via HTTP headers (`X-Tenant-ID`)
   - Isolated user and token data per tenant
   - Tenant-aware authorization

5. **Key Management**
   - Background service for key rollover
   - Automatic JWKS endpoint updates
   - Development certificates (replace with production certificates)

## Getting Started

### Prerequisites

- .NET 8.0 SDK
- Redis server (optional, defaults to localhost:6379)

### Running the Application

```bash
# Clone the repository
git clone https://github.com/ta3n/OpenIddictSample.git
cd OpenIddictSample

# Build the solution
dotnet build

# Run the application
dotnet run --project src/OpenIddictSample/OpenIddictSample.csproj
```

The server will start on `https://localhost:7001` and `http://localhost:5001`.

### Configuration

Edit `appsettings.json` to configure:

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

## Usage Examples

### Authorization Code Flow

1. **Authorization Request**
```
GET /connect/authorize?
    client_id=test-client&
    response_type=code&
    redirect_uri=https://localhost:7001/callback&
    scope=openid profile api&
    tenant=default
```

2. **Token Exchange**
```bash
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code&
client_id=test-client&
client_secret=test-secret&
code=<authorization_code>&
redirect_uri=https://localhost:7001/callback
```

### Password Grant (for testing)

```bash
curl -X POST https://localhost:7001/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -H "X-Tenant-ID: default" \
  -d "grant_type=password&username=testuser&password=password&client_id=test-client&client_secret=test-secret&scope=api"
```

### Refresh Token

```bash
curl -X POST https://localhost:7001/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -H "X-Tenant-ID: default" \
  -d "grant_type=refresh_token&refresh_token=<refresh_token>&client_id=test-client&client_secret=test-secret"
```

### Token Revocation

```bash
curl -X POST https://localhost:7001/connect/revoke \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -H "X-Tenant-ID: default" \
  -d "token=<token>&client_id=test-client&client_secret=test-secret"
```

### Logout

```bash
curl -X POST https://localhost:7001/connect/logout \
  -H "X-Tenant-ID: default"
```

## Multi-Tenant Usage

To use different tenants, include the `X-Tenant-ID` header in your requests:

```bash
curl -H "X-Tenant-ID: tenant1" https://localhost:7001/connect/authorize?...
curl -H "X-Tenant-ID: tenant2" https://localhost:7001/connect/token -d ...
```

Each tenant has isolated:
- Users
- Tokens
- Refresh tokens
- Authorization grants

## Security Features

### Refresh Token Rotation

When a refresh token is used, it is automatically revoked and a new one is issued. This prevents token replay attacks.

### Token Storage

- Access tokens: Stored in Redis with TTL
- Refresh tokens: Stored in database with revocation tracking
- Token reuse detection: Revoked tokens cannot be reused

### Key Rollover

The `KeyRolloverService` background worker:
- Monitors key age (default: 90 days)
- Generates new signing keys automatically
- Maintains old keys for grace period
- Updates JWKS endpoint dynamically

## Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/connect/authorize` | GET, POST | Authorization endpoint |
| `/connect/token` | POST | Token endpoint |
| `/connect/revoke` | POST | Revocation endpoint |
| `/connect/logout` | GET, POST | Logout endpoint |
| `/.well-known/openid-configuration` | GET | OpenID Connect discovery |
| `/.well-known/jwks` | GET | JSON Web Key Set |
| `/api/protected` | GET | Example protected resource |
| `/health` | GET | Health check |

## Development

### Database

The application uses SQLite for simplicity. The database is automatically created on startup at `openiddict.db`.

### Test Client

A test client is automatically registered on startup:
- Client ID: `test-client`
- Client Secret: `test-secret`
- Redirect URI: `https://localhost:7001/callback`

### Test User

A test user is automatically created:
- Username: `testuser`
- Password: `password`
- Tenant: `default`

## Production Deployment

For production deployment:

1. Replace development certificates with production certificates
2. Configure a production Redis instance
3. Use a production database (PostgreSQL, SQL Server, etc.)
4. Enable HTTPS enforcement
5. Configure proper CORS policies
6. Set up key management with Azure Key Vault or HSM
7. Implement proper logging and monitoring
8. Configure rate limiting and DDoS protection

## License

MIT

## References

- [OpenIddict Documentation](https://documentation.openiddict.com/)
- [OAuth 2.0 RFC 6749](https://tools.ietf.org/html/rfc6749)
- [OpenID Connect Core](https://openid.net/specs/openid-connect-core-1_0.html)