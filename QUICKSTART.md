# 🚀 Quick Start Guide - OpenIddict Sample

## Bước 1: Khởi động Redis và SQL Server

```bash
# Chạy Docker Compose để start Redis và SQL Server
docker-compose up -d sqlserver redis

# Kiểm tra services đã chạy
docker ps
```

## Bước 2: Restore Dependencies

```bash
dotnet restore
```

## Bước 3: Chạy Ứng Dụng

```bash
dotnet run
```

Ứng dụng sẽ chạy tại: **https://localhost:5001**

## Bước 4: Đăng Ký User Đầu Tiên

1. Mở trình duyệt: https://localhost:5001/Account/Register
2. Nhập thông tin:
   - Username: `testuser`
   - Email: `test@example.com`
   - Password: `Test123!`
   - Tenant ID: `tenant1` (mặc định)
3. Click "Register"

## Bước 5: Test Authorization Code Flow

### Sử dụng Postman hoặc Browser:

**Step 1: Authorization Request**

Mở URL sau trong browser (đảm bảo đã đăng nhập):

```
https://localhost:5001/connect/authorize?client_id=postman-client&redirect_uri=https://oauth.pstmn.io/v1/callback&response_type=code&scope=openid%20profile%20email%20api&state=xyz123
```

Thêm header: `X-Tenant-ID: tenant1`

**Step 2: Token Exchange**

Sau khi có authorization code, dùng Postman:

```http
POST https://localhost:5001/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code
&code={YOUR_AUTHORIZATION_CODE}
&redirect_uri=https://oauth.pstmn.io/v1/callback
&client_id=postman-client
&client_secret=postman-secret
```

Bạn sẽ nhận được:
```json
{
  "access_token": "eyJ...",
  "token_type": "Bearer",
  "expires_in": 1800,
  "refresh_token": "CfDJ8...",
  "id_token": "eyJ..."
}
```

## Bước 6: Test Refresh Token

```http
POST https://localhost:5001/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token
&refresh_token={YOUR_REFRESH_TOKEN}
&client_id=postman-client
&client_secret=postman-secret
```

Token cũ sẽ bị revoke, bạn nhận được token mới.

## Bước 7: Test Token Revocation

```http
POST https://localhost:5001/connect/revoke
Content-Type: application/x-www-form-urlencoded

token={YOUR_REFRESH_TOKEN}
&client_id=postman-client
&client_secret=postman-secret
```

## 📋 Checklist

- [ ] Docker đang chạy
- [ ] Redis đang chạy (port 6379)
- [ ] SQL Server đang chạy (port 1433)
- [ ] Đã restore packages
- [ ] Đã đăng ký user
- [ ] Đã test authorization flow
- [ ] Đã test refresh token rotation
- [ ] Đã test token revocation

## 🛠️ Troubleshooting

### Redis connection error?
```bash
docker logs openiddict_redis
redis-cli ping  # Should return PONG
```

### SQL Server connection error?
```bash
docker logs openiddict_sqlserver
# Check connection string in appsettings.json
```

### Cannot login?
- Đảm bảo gửi header `X-Tenant-ID: tenant1`
- Check user đã được tạo trong database

## 📚 Đọc Thêm

- Chi tiết đầy đủ: [README.md](README.md)
- OpenIddict Docs: https://documentation.openiddict.com/

---

**Tip:** Sử dụng Postman collection hoặc curl để test các endpoints dễ dàng hơn!
