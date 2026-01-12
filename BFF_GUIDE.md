# 🔐 BFF (Backend For Frontend) Pattern Guide

## Tổng Quan

BFF pattern được triển khai để tăng cường bảo mật cho SPAs và Mobile Apps bằng cách:

- 🔒 Lưu tokens ở server-side (không expose cho frontend)
- 🍪 Sử dụng secure HTTP-only cookies cho session
- 🛡️ Proxy API calls để thêm access token tự động
- 🔐 CSRF protection cho state-changing operations
- 🚫 Ngăn chặn XSS attacks vào tokens

## Kiến Trúc

```
┌─────────────┐         ┌──────────────┐         ┌──────────────┐
│   Frontend  │ ◄─────► │  BFF Server  │ ◄─────► │  Backend API │
│   (SPA)     │ Cookies │  (This App)  │ Tokens  │  (Protected) │
└─────────────┘         └──────────────┘         └──────────────┘
```

**Flow:**

1. Frontend gọi BFF endpoints (không cần handle tokens)
2. BFF lưu tokens trong Redis session
3. BFF proxy requests đến Backend APIs với access token
4. BFF auto-refresh expired tokens

## 🚀 Endpoints

### 1. Login

```http
POST /bff/login
Content-Type: application/json

{
  "username": "testuser",
  "password": "password123",
  "tenantId": "tenant1"  // optional
}
```

**Response:**

```json
{
  "success": true,
  "user": {
    "userId": "...",
    "username": "testuser",
    "email": "testuser@example.com",
    "tenantId": "tenant1"
  }
}
```

**Set-Cookie:**

```
bff_session=<session_id>; HttpOnly; Secure; SameSite=Strict
```

### 2. Get Current User

```http
GET /bff/user
Cookie: bff_session=<session_id>
```

**Response:**

```json
{
  "userId": "...",
  "username": "testuser",
  "email": "testuser@example.com",
  "tenantId": "tenant1",
  "authenticated": true
}
```

### 3. Check Authentication

```http
GET /bff/auth/check
Cookie: bff_session=<session_id>
```

**Response:**

```json
{
  "authenticated": true
}
```

### 4. Logout

```http
POST /bff/logout
Cookie: bff_session=<session_id>
```

**Response:**

```json
{
  "success": true
}
```

### 5. API Proxy

Tất cả requests đến backend API đều proxy qua BFF:

```http
GET /bff/api/resource/me
Cookie: bff_session=<session_id>
```

BFF tự động:

- Thêm `Authorization: Bearer <access_token>` header
- Refresh token nếu cần
- Forward request đến `/api/resource/me`

**Supports all HTTP methods:**

- GET /bff/api/{path}
- POST /bff/api/{path}
- PUT /bff/api/{path}
- DELETE /bff/api/{path}
- PATCH /bff/api/{path}

### 6. Get Anti-forgery Token

```http
GET /bff/antiforgery
```

**Response:**

```json
{
  "token": "<csrf_token>",
  "headerName": "X-CSRF-TOKEN"
}
```

Sử dụng token này cho POST/PUT/DELETE requests:

```http
POST /bff/api/something
X-CSRF-TOKEN: <csrf_token>
```

## 💻 Frontend Integration

### React Example

```typescript
// api.ts
const API_BASE = 'https://localhost:5001/bff';

class BffApiClient {
  // Login
  async login(username: string, password: string, tenantId?: string) {
    const response = await fetch(`${API_BASE}/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include', // Important: send cookies
      body: JSON.stringify({ username, password, tenantId })
    });
    return response.json();
  }

  // Get current user
  async getCurrentUser() {
    const response = await fetch(`${API_BASE}/user`, {
      credentials: 'include'
    });

    if (!response.ok) {
      throw new Error('Not authenticated');
    }

    return response.json();
  }

  // Check auth status
  async isAuthenticated() {
    const response = await fetch(`${API_BASE}/auth/check`, {
      credentials: 'include'
    });
    const data = await response.json();
    return data.authenticated;
  }

  // Logout
  async logout() {
    await fetch(`${API_BASE}/logout`, {
      method: 'POST',
      credentials: 'include'
    });
  }

  // Call protected API through BFF proxy
  async callApi(path: string, options: RequestInit = {}) {
    const response = await fetch(`${API_BASE}/api/${path}`, {
      ...options,
      credentials: 'include',
      headers: {
        ...options.headers,
        'Content-Type': 'application/json'
      }
    });

    if (response.status === 401) {
      // Session expired, redirect to login
      window.location.href = '/login';
      throw new Error('Session expired');
    }

    return response.json();
  }

  // Get CSRF token
  async getCsrfToken() {
    const response = await fetch(`${API_BASE}/antiforgery`, {
      credentials: 'include'
    });
    const data = await response.json();
    return data.token;
  }

  // POST with CSRF protection
  async postWithCsrf(path: string, body: any) {
    const csrfToken = await this.getCsrfToken();

    return fetch(`${API_BASE}/api/${path}`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-CSRF-TOKEN': csrfToken
      },
      body: JSON.stringify(body)
    });
  }
}

export const api = new BffApiClient();
```

### Usage in Components

```typescript
// LoginComponent.tsx
import { api } from './api';

function LoginComponent() {
  const handleLogin = async (e: FormEvent) => {
    e.preventDefault();

    try {
      const result = await api.login(username, password, 'tenant1');

      if (result.success) {
        // Redirect to dashboard
        window.location.href = '/dashboard';
      }
    } catch (error) {
      console.error('Login failed', error);
    }
  };

  return (
    <form onSubmit={handleLogin}>
      <input type="text" name="username" />
      <input type="password" name="password" />
      <button type="submit">Login</button>
    </form>
  );
}
```

```typescript
// DashboardComponent.tsx
import { api } from './api';
import { useEffect, useState } from 'react';

function DashboardComponent() {
  const [user, setUser] = useState(null);
  const [data, setData] = useState([]);

  useEffect(() => {
    // Get current user
    api.getCurrentUser().then(setUser);

    // Fetch protected data through BFF proxy
    api.callApi('resource/data').then(setData);
  }, []);

  const handleLogout = async () => {
    await api.logout();
    window.location.href = '/login';
  };

  return (
    <div>
      <h1>Welcome, {user?.username}</h1>
      <button onClick={handleLogout}>Logout</button>
      <div>{/* Render data */}</div>
    </div>
  );
}
```

### Protected Route Component

```typescript
// ProtectedRoute.tsx
import { useEffect, useState } from 'react';
import { api } from './api';

function ProtectedRoute({ children }) {
  const [authenticated, setAuthenticated] = useState(false);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api.isAuthenticated()
      .then(setAuthenticated)
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return <div>Loading...</div>;
  }

  if (!authenticated) {
    window.location.href = '/login';
    return null;
  }

  return children;
}
```

## 🔧 Configuration

### appsettings.json

```json
{
  "OpenIddict": {
    "TokenEndpoint": "https://localhost:5001/connect/token",
    "ClientId": "postman-client",
    "ClientSecret": "postman-secret"
  }
}
```

### CORS Configuration

Thêm origin của frontend SPA vào `Program.cs`:

```csharp
policy.WithOrigins(
    "http://localhost:3000",  // Your React app
    "https://your-production-domain.com"
)
```

## 🛡️ Security Features

### 1. HTTP-Only Cookies

```
Set-Cookie: bff_session=...; HttpOnly; Secure; SameSite=Strict
```

- `HttpOnly`: JavaScript không thể access
- `Secure`: Chỉ gửi qua HTTPS
- `SameSite=Strict`: CSRF protection

### 2. CSRF Protection

```http
X-CSRF-TOKEN: <token_from_/bff/antiforgery>
```

### 3. Security Headers

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `X-XSS-Protection: 1; mode=block`
- `Content-Security-Policy: ...`
- `Referrer-Policy: strict-origin-when-cross-origin`

### 4. Automatic Token Refresh

BFF tự động refresh access token khi:

- Token sắp expire (còn < 5 phút)
- Token đã expire

### 5. Session Management

Sessions được lưu trong Redis với:

- Auto expiration (12 hours)
- Last accessed time tracking
- Automatic cleanup

## 📊 Session Storage

**Redis Structure:**

```
bff_session:<session_id> -> {
  userId: string,
  username: string,
  email: string,
  tenantId: string,
  accessToken: string,      // Stored server-side only
  refreshToken: string,     // Stored server-side only
  idToken: string,
  accessTokenExpiresAt: timestamp,
  createdAt: timestamp,
  lastAccessedAt: timestamp
}
```

## 🚦 Error Handling

### Frontend Error Responses

**401 Unauthorized:**

```json
{
  "error": "no_session"
}
// or
{
  "error": "session_expired"
}
```

→ Redirect to login page

**400 Bad Request:**

```json
{
  "error": "invalid_tenant"
}
```

**500 Internal Server Error:**

```json
{
  "error": "proxy_error"
}
```

## 🧪 Testing với Postman

### 1. Login

```
POST https://localhost:5001/bff/login
Body:
{
  "username": "testuser",
  "password": "password123",
  "tenantId": "tenant1"
}
```

**Save the cookie** từ response.

### 2. Get User Info

```
GET https://localhost:5001/bff/user
Cookie: bff_session=<saved_from_login>
```

### 3. Call Protected API

```
GET https://localhost:5001/bff/api/resource/me
Cookie: bff_session=<saved_from_login>
```

### 4. Logout

```
POST https://localhost:5001/bff/logout
Cookie: bff_session=<saved_from_login>
```

## 📈 Benefits

✅ **Enhanced Security:**

- Tokens never exposed to frontend
- XSS attacks can't steal tokens
- HTTP-only cookies

✅ **Simplified Frontend:**

- No token management needed
- No refresh logic required
- Just use cookies

✅ **Centralized Auth:**

- All auth logic in one place
- Easy to update/maintain
- Consistent across all frontends

✅ **Better UX:**

- Automatic token refresh
- Seamless session management
- No "token expired" errors to user

## 🔄 Migration from Token-based to BFF

**Before (Token-based):**

```typescript
// Frontend stores tokens
localStorage.setItem('access_token', token);

// Frontend adds auth header
fetch('/api/data', {
  headers: {
    'Authorization': `Bearer ${token}`
  }
});

// Frontend handles refresh
if (isTokenExpired(token)) {
  token = await refreshToken();
}
```

**After (BFF):**

```typescript
// No token handling needed!
fetch('/bff/api/data', {
  credentials: 'include'  // Just send cookies
});

// BFF handles everything automatically
```

## 📚 References

- [BFF Pattern](https://datatracker.ietf.org/doc/html/draft-ietf-oauth-browser-based-apps)
- [OAuth for Browser-Based Apps](https://oauth.net/2/browser-based-apps/)
- [OWASP Token Binding](https://owasp.org/www-community/controls/Token_Binding)

---

**Lưu ý:** Đây là implementation cơ bản. Trong production, cần:

- Integrate với actual OAuth flow thay vì dummy tokens
- Thêm rate limiting
- Thêm logging và monitoring
- Implement proper error handling
- Add unit tests và integration tests
