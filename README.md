# OpenIddict Sample 2 - Hướng Dẫn Chi Tiết

Dự án này triển khai đầy đủ các tính năng OAuth 2.0 và OpenID Connect sử dụng OpenIddict, bao gồm:

## 🎯 Các Tính Năng Đã Triển Khai

### 1. **Authorization Code Flow** ✅
Authorization Code Flow là luồng OAuth 2.0 được khuyến nghị cho các ứng dụng web server-side.

**Cách hoạt động:**
1. Client chuyển hướng user đến `/connect/authorize`
2. User đăng nhập và xác thực (qua Cookie Authentication)
3. Server tạo authorization code và redirect về client
4. Client đổi authorization code lấy access token tại `/connect/token`

**Endpoint:** 
- Authorization: `GET/POST /connect/authorize`
- Token: `POST /connect/token`

**Ví dụ request:**
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
Refresh Token Rotation tăng cường bảo mật bằng cách tạo refresh token mới mỗi lần sử dụng.

**Cách hoạt động:**
1. Client sử dụng refresh token để lấy access token mới
2. Server revoke refresh token cũ ngay lập tức
3. Server tạo và trả về refresh token mới kèm access token
4. Token mới được lưu trong Redis với metadata rotation

**Code implementation:** Xem `AuthorizationController.HandleRefreshTokenAsync()`

**Redis storage structure:**
```
refresh_token:{token_id} -> {
  TokenId, UserId, TenantId,
  PreviousTokenId,  // Tracking rotation chain
  IssuedAt, ExpiresAt,
  RotationCount
}
```

**Ví dụ request:**
```http
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token
&refresh_token={your_refresh_token}
&client_id=postman-client
&client_secret=postman-secret
```

### 3. **Logout / Revoke** ✅
Hỗ trợ đăng xuất và thu hồi token.

**Logout Endpoint:**
- URL: `GET/POST /connect/logout`
- Chức năng: Sign out user, revoke tất cả refresh tokens của user
- Redirect về `post_logout_redirect_uri`

**Revoke Endpoint:**
- URL: `POST /connect/revoke`
- Chức năng: Thu hồi một token cụ thể (access hoặc refresh token)

**Ví dụ:**
```http
POST /connect/revoke
Content-Type: application/x-www-form-urlencoded

token={refresh_token_to_revoke}
&client_id=postman-client
&client_secret=postman-secret
```

### 4. **Redis-backed Token Storage** ✅
Tất cả refresh tokens được lưu trong Redis thay vì database.

**Lợi ích:**
- ⚡ Hiệu suất cao (in-memory cache)
- 🔄 Hỗ trợ token rotation và revocation nhanh
- 📊 Tự động expiration dựa vào TTL của Redis
- 🗑️ Dễ dàng xóa tokens khi cần

**Service:** `TokenStorageService` trong [Services/TokenStorageService.cs](Services/TokenStorageService.cs)

**Redis Keys:**
```
refresh_token:{token_id}        # Token data
user_tokens:{user_id}           # List of user's tokens
revoked:{token_id}              # Revoked token blacklist
```

### 5. **Multi-Tenant Isolation** ✅
Hỗ trợ nhiều tenant với cách ly dữ liệu hoàn toàn.

**Cách xác định Tenant:**
1. **Header:** `X-Tenant-ID: tenant1`
2. **Subdomain:** `tenant1.yourdomain.com` (subdomain = tenant ID)
3. **Claims:** `tenant_id` trong JWT token

**Tenant validation:**
- Mỗi user thuộc về một tenant cụ thể
- Authorization request phải chỉ định tenant ID
- User chỉ có thể access resources trong tenant của mình

**Code implementation:** Xem `TenantService` trong [Services/TenantService.cs](Services/TenantService.cs)

**Database schema:**
```sql
-- Users table
UserId, Username, Email, TenantId

-- Tenants table
TenantId, Name, Domain, IsActive, SigningKeyId
```

### 6. **Key Rollover & JWKS Rotation** ✅
Tự động rotation signing keys để tăng cường bảo mật.

**Cách hoạt động:**
1. Mỗi tenant có thể có signing key riêng
2. Keys được lưu trong Redis với expiration (90 days)
3. Khi key sắp hết hạn, system tự động tạo key mới
4. Giữ lại keys cũ trong grace period (30 days) để validate old tokens
5. JWKS endpoint trả về tất cả valid keys

**Service:** `KeyRotationService` trong [Services/KeyRotationService.cs](Services/KeyRotationService.cs)

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

## 🏗️ Kiến Trúc Dự Án

```
OpenIddictSample2/
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

## 🚀 Cách Chạy Dự Án

### Yêu Cầu:
- .NET 8.0 SDK
- SQL Server (hoặc SQL Server Express)
- Redis Server

### Bước 1: Cài đặt Redis

**Windows:**
```powershell
# Sử dụng Windows Subsystem for Linux (WSL) hoặc Docker
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

### Bước 2: Cấu hình Connection Strings

Chỉnh sửa `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=OpenIddictSample;User Id=sa;Password=YourPassword;TrustServerCertificate=True",
    "Redis": "localhost:6379"
  }
}
```

### Bước 3: Restore Packages và Chạy

```bash
dotnet restore
dotnet build
dotnet run
```

Server sẽ chạy tại: `https://localhost:5001`

### Bước 4: Seed Data

Khi chạy lần đầu, dự án tự động:
- Tạo database
- Seed tenant mặc định (`tenant1`)
- Tạo OAuth client (`postman-client`)
- Tạo scopes (`api`, `email`, `profile`)

## 🧪 Testing với Postman

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

## 🔐 Bảo Mật Best Practices

### 1. Production Configuration

Trong production, **không sử dụng** development certificates:

```csharp
// ❌ CHỈ dùng trong Development
options.AddDevelopmentEncryptionCertificate()
       .AddDevelopmentSigningCertificate();

// ✅ Production
options.AddEncryptionCertificate(cert)
       .AddSigningCertificate(cert);

// HOẶC sử dụng Key Rotation Service
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

- ✅ Validate tenant ID trong mọi request
- ✅ Filter data theo tenant trong queries
- ✅ Include tenant_id trong JWT claims
- ✅ Separate signing keys per tenant (optional)

### 4. Token Security

- ✅ Sử dụng HTTPS only
- ✅ Short-lived access tokens (15-30 phút)
- ✅ Longer-lived refresh tokens (7-30 ngày)
- ✅ Enable refresh token rotation
- ✅ Revoke tokens khi logout
- ✅ Store tokens securely (Redis với encryption)

## 📚 Các Endpoint Chính

| Endpoint | Method | Mô Tả |
|----------|--------|-------|
| `/connect/authorize` | GET/POST | Authorization Code endpoint |
| `/connect/token` | POST | Token exchange endpoint |
| `/connect/revoke` | POST | Token revocation |
| `/connect/logout` | GET/POST | Logout endpoint |
| `/connect/userinfo` | GET | User info endpoint |
| `/Account/Login` | GET/POST | User login |
| `/Account/Register` | GET/POST | User registration |

## 🐛 Troubleshooting

### Issue: "Cannot connect to Redis"
```bash
# Check Redis is running
redis-cli ping
# Should return: PONG
```

### Issue: "Database connection failed"
- Kiểm tra SQL Server đang chạy
- Verify connection string trong appsettings.json
- Check firewall settings

### Issue: "Invalid tenant"
- Đảm bảo gửi header `X-Tenant-ID` trong request
- Verify tenant exists trong database
- Check tenant IsActive = true

## 📖 Tài Liệu Tham Khảo

- [OpenIddict Documentation](https://documentation.openiddict.com/)
- [OAuth 2.0 RFC 6749](https://tools.ietf.org/html/rfc6749)
- [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html)
- [Refresh Token Rotation](https://auth0.com/docs/secure/tokens/refresh-tokens/refresh-token-rotation)

## 🤝 Đóng Góp

Nếu bạn có câu hỏi hoặc đề xuất cải tiến, vui lòng tạo issue hoặc pull request.

## 📝 License

MIT License - Free to use and modify.

---

**Lưu ý:** Đây là sample project cho mục đích học tập. Trong production, cần thêm nhiều lớp bảo mật và xử lý lỗi chi tiết hơn.
