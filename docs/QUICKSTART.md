# 🚀 Quick Start Guide - OpenIddict Sample

## Step 1: Start Redis and PostgreSQL

```bash
# Run Docker Compose to start Redis and PostgreSQL
docker-compose up -d postgres redis

# Check if services are running
docker ps
```

## Step 2: Restore Dependencies

```bash
dotnet restore
```

## Step 3: Run the Application

```bash
dotnet run
```

The application will run at: **https://localhost:5001**

## Step 4: Register the First User

1. Open the browser: https://localhost:5001/Account/Register
2. Enter the following information:
  - Username: `testuser`
  - Email: `test@example.com`
  - Password: `Test123!`
  - Tenant ID: `tenant1` (default)
3. Click "Register"

## Step 5: Test Authorization Code Flow

### Using Postman or Browser:

**Step 1: Authorization Request**

Open the following URL in the browser (ensure you are logged in):

```
https://localhost:5001/connect/authorize?client_id=postman-client&redirect_uri=https://oauth.pstmn.io/v1/callback&response_type=code&scope=openid%20profile%20email%20api&state=xyz123
```

Add the header: `X-Tenant-ID: tenant1`

**Step 2: Token Exchange**

After obtaining the authorization code, use Postman:

```http
POST https://localhost:5001/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code
&code={YOUR_AUTHORIZATION_CODE}
&redirect_uri=https://oauth.pstmn.io/v1/callback
&client_id=postman-client
&client_secret=postman-secret
```

You will receive:

```json
{
  "access_token": "eyJ...",
  "token_type": "Bearer",
  "expires_in": 1800,
  "refresh_token": "CfDJ8...",
  "id_token": "eyJ..."
}
```

## Step 6: Test Refresh Token

```http
POST https://localhost:5001/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token
&refresh_token={YOUR_REFRESH_TOKEN}
&client_id=postman-client
&client_secret=postman-secret
```

The old token will be revoked, and you will receive a new token.

## Step 7: Test Token Revocation

```http
POST https://localhost:5001/connect/revoke
Content-Type: application/x-www-form-urlencoded

token={YOUR_REFRESH_TOKEN}
&client_id=postman-client
&client_secret=postman-secret
```

## 📋 Checklist

- [ ] Docker is running
- [ ] Redis is running (port 6379)
- [ ] PostgreSQL is running (port 5432)
- [ ] Packages have been restored
- [ ] User has been registered
- [ ] Authorization flow has been tested
- [ ] Refresh token rotation has been tested
- [ ] Token revocation has been tested

## 🛠️ Troubleshooting

### Redis connection error?

```bash
docker logs openiddict_redis
redis-cli ping  # Should return PONG
```

### PostgreSQL connection error?

```bash
docker logs openiddict_postgres
# Check connection string in appsettings.json
```

### Cannot log in?

- Ensure the `X-Tenant-ID: tenant1` header is sent
- Check if the user has been created in the database

## 📚 Read More

- OpenIddict Docs: https://documentation.openiddict.com/

---

**Tip:** Use Postman collections or curl to test endpoints more easily!
