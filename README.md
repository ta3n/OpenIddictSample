# OpenIddict Sample - Detailed Guide

This project fully implements OAuth 2.0 and OpenID Connect features using OpenIddict, including:

## 🎯 Features Implemented

### 1. **Authorization Code Flow** ✅

Authorization Code Flow is the recommended OAuth 2.0 flow for server-side web applications.

**How it works:**

1. The client redirects the user to `/connect/authorize`
2. The user logs in and authenticates (via Cookie Authentication)
3. The server generates an authorization code and redirects back to the client
4. The client exchanges the authorization code for an access token at `/connect/token`

**Endpoint:**

- Authorization: `GET/POST /connect/authorize`
- Token: `POST /connect/token`

**Example request:**

```http
GET /connect/authorize?
  client_id=postman-client
  &redirect_uri=https://oauth.pstmn.io/v1/callback
  &response_type=code
  &scope=openid profile email api
  &state=random_state_value
  &tenant_id=tenant1

Header: X-Tenant-ID: tenant1
```

### 2. **Refresh Token Rotation** ✅

Refresh Token Rotation enhances security by generating a new refresh token each time it is used.

**How it works:**

1. The client uses the refresh token to obtain a new access token
2. The server immediately revokes the old refresh token
3. The server generates and returns a new refresh token along with the access token
4. The new token is stored in Redis with rotation metadata

**Code implementation:** See `AuthorizationController.HandleRefreshTokenAsync()`

**Redis storage structure:**

```
refresh_token:{token_id} -> {
  TokenId, UserId, TenantId,
  PreviousTokenId,  // Tracking rotation chain
  IssuedAt, ExpiresAt,
  RotationCount
}
```

**Example request:**

```http
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token
&refresh_token={your_refresh_token}
&client_id=postman-client
&client_secret=postman-secret
```

### 3. **Logout / Revoke** ✅

Supports user logout and token revocation.

**Logout Endpoint:**

- URL: `GET/POST /connect/logout`
- Functionality: Signs out the user, revokes all the user's refresh tokens
- Redirects to `post_logout_redirect_uri`

**Revoke Endpoint:**

- URL: `POST /connect/revoke`
- Functionality: Revokes a specific token (access or refresh token)

**Example:**

```http
POST /connect/revoke
Content-Type: application/x-www-form-urlencoded

token={refresh_token_to_revoke}
&client_id=postman-client
&client_secret=postman-secret
```

### 4. **Redis-backed Token Storage** ✅

All refresh tokens are stored in Redis instead of a database.

**Benefits:**

- ⚡ High performance (in-memory cache)
- 🔄 Supports fast token rotation and revocation
- 📊 Automatic expiration based on Redis TTL
- 🗑️ Easy token deletion when needed

**Service:** `TokenStorageService` in [Services/TokenStorageService.cs](Services/TokenStorageService.cs)

**Redis Keys:**

```
refresh_token:{token_id}        # Token data
user_tokens:{user_id}           # List of user's tokens
revoked:{token_id}              # Revoked token blacklist
```

### 5. **Multi-Tenant Isolation** ✅

Supports multiple tenants with complete data isolation.

**How to identify Tenant:**

1. **Header:** `X-Tenant-ID: tenant1`
2. **Subdomain:** `tenant1.yourdomain.com` (subdomain = tenant ID)
3. **Claims:** `tenant_id` in the JWT token

**Tenant validation:**

- Each user belongs to a specific tenant
- Authorization requests must specify the tenant ID
- Users can only access resources within their tenant

**Code implementation:** See `TenantService` in [Services/TenantService.cs](Services/TenantService.cs)

**Database schema:**

```sql
-- Users table
UserId, Username, Email, TenantId

-- Tenants table
TenantId, Name, Domain, IsActive, SigningKeyId
```

### 6. **Key Rollover & JWKS Rotation** ✅

Automatically rotates signing keys to enhance security.

**How it works:**

1. Each tenant can have its own signing key
2. Keys are stored in Redis with expiration (90 days)
3. When a key is about to expire, the system automatically generates a new key
4. Old keys are retained during a grace period (30 days) to validate old tokens
5. The JWKS endpoint returns all valid keys

**Service:** `KeyRotationService` in [Services/KeyRotationService.cs](Services/KeyRotationService.cs)

**Key lifecycle:**

```
Day 0: Create Key A (current)
Day 90: Create Key B (current), Key A (valid for verification)
Day 120: Key A expired, remove from JWKS
```

**Manual rotation:**

```csharp
await keyRotationService.RotateKeysAsync("tenant1");
```

## 🏗️ Project Architecture

```
OpenIddictSample/
├── Controllers/
│   ├── AuthorizationController.cs  # OAuth endpoints
│   ├── AccountController.cs        # Login/Register
│   └── HomeController.cs
├── Services/
│   ├── TenantService.cs           # Multi-tenant logic
│   ├── TokenStorageService.cs     # Redis token storage
│   └── KeyRotationService.cs      # Key rotation
├── Models/
│   ├── ApplicationUser.cs         # User with TenantId
│   └── Tenant.cs
├── Data/
│   └── ApplicationDbContext.cs    # EF Core + OpenIddict
└── Program.cs                     # Configuration
```

## 🚀 How to Run the Project

### Requirements:

- .NET 8.0 SDK
- PostgreSQL (or Docker)
- Redis Server

### Step 1: Install Redis

**Windows:**

```powershell
# Use Windows Subsystem for Linux (WSL) or Docker
docker run -d -p 6379:6379 --name redis redis:latest
```

**macOS:**

```bash
brew install redis
brew services start redis
```

**Linux:**

```bash
sudo apt-get install redis-server
sudo systemctl start redis
```

### Step 2: Configure Connection Strings

Edit `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=OpenIddictSample;Username=postgres;Password=YourPassword;",
    "Redis": "localhost:6379"
  }
}
```

### Step 3: Restore Packages and Run

```bash
dotnet restore
dotnet build
dotnet run
```

The server will run at: `https://localhost:5001`

### Step 4: Seed Data

On the first run, the project automatically:

- Creates the database
- Seeds a default tenant (`tenant1`)
- Creates an OAuth client (`postman-client`)
- Creates scopes (`api`, `email`, `profile`)

## 🧪 Testing with Postman

### 1. Test Authorization Code Flow

**Step 1: Authorize**

```
GET https://localhost:5001/connect/authorize?
  client_id=postman-client
  &redirect_uri=https://oauth.pstmn.io/v1/callback
  &response_type=code
  &scope=openid profile email api
  &state=xyz

Headers:
  X-Tenant-ID: tenant1
```

**Step 2: Exchange Code for Token**

```http
POST https://localhost:5001/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code
&code={authorization_code}
&redirect_uri=https://oauth.pstmn.io/v1/callback
&client_id=postman-client
&client_secret=postman-secret
```

**Response:**

```json
{
  "access_token": "eyJ...",
  "token_type": "Bearer",
  "expires_in": 1800,
  "refresh_token": "CfDJ8...",
  "id_token": "eyJ..."
}
```

### 2. Test Refresh Token Rotation

```http
POST https://localhost:5001/connect/token

grant_type=refresh_token
&refresh_token={old_refresh_token}
&client_id=postman-client
&client_secret=postman-secret
```

**Response:** New access token + new refresh token (old token is revoked)

### 3. Test Token Revocation

```http
POST https://localhost:5001/connect/revoke

token={refresh_token}
&client_id=postman-client
&client_secret=postman-secret
```

### 4. Test Logout

```http
POST https://localhost:5001/connect/logout?
  post_logout_redirect_uri=https://localhost:5001

Headers:
  Cookie: {your_auth_cookie}
```

## 🔐 Security Best Practices

### 1. Production Configuration

In production, **do not use** development certificates:

```csharp
// ❌ ONLY for Development
options.AddDevelopmentEncryptionCertificate()
       .AddDevelopmentSigningCertificate();

// ✅ Production
options.AddEncryptionCertificate(cert)
       .AddSigningCertificate(cert);

// OR use the Key Rotation Service
var signingKey = await keyRotationService.GetCurrentSigningKeyAsync(tenantId);
options.AddSigningCredentials(signingKey);
```

### 2. Redis Security

```json
{
  "ConnectionStrings": {
    "Redis": "host:6379,password=strongpassword,ssl=true,abortConnect=false"
  }
}
```

### 3. Tenant Isolation Checklist

- ✅ Validate tenant ID in every request
- ✅ Filter data by tenant in queries
- ✅ Include tenant_id in JWT claims
- ✅ Separate signing keys per tenant (optional)

### 4. Token Security

- ✅ Use HTTPS only
- ✅ Short-lived access tokens (15-30 minutes)
- ✅ Longer-lived refresh tokens (7-30 days)
- ✅ Enable refresh token rotation
- ✅ Revoke tokens on logout
- ✅ Store tokens securely (Redis with encryption)

## 📚 Key Endpoints

| Endpoint             | Method   | Description                 |
|----------------------|----------|-----------------------------|
| `/connect/authorize` | GET/POST | Authorization Code endpoint |
| `/connect/token`     | POST     | Token exchange endpoint     |
| `/connect/revoke`    | POST     | Token revocation            |
| `/connect/logout`    | GET/POST | Logout endpoint             |
| `/connect/userinfo`  | GET      | User info endpoint          |
| `/Account/Login`     | GET/POST | User login                  |
| `/Account/Register`  | GET/POST | User registration           |

## 🐛 Troubleshooting

### Issue: "Cannot connect to Redis"

```bash
# Check Redis is running
redis-cli ping
# Should return: PONG
```

### Issue: "Database connection failed"

- Check if the SQL Server is running
- Verify the connection string in appsettings.json
- Check firewall settings

### Issue: "Invalid tenant"

- Ensure the `X-Tenant-ID` header is sent in the request
- Verify the tenant exists in the database
- Check if the tenant IsActive = true

## 📖 References

- [OpenIddict Documentation](https://documentation.openiddict.com/)
- [OAuth 2.0 RFC 6749](https://tools.ietf.org/html/rfc6749)
- [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html)
- [Refresh Token Rotation](https://auth0.com/docs/secure/tokens/refresh-tokens/refresh-token-rotation)
