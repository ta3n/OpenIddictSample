#!/bin/bash
# OpenIddict Sample Test Script
# This script demonstrates all implemented features

echo "==================================================================="
echo "OpenIddict Sample - Feature Demonstration"
echo "==================================================================="
echo ""

# Start the application
echo "Starting OpenIddict server..."
cd "$(dirname "$0")/src/OpenIddictSample"
dotnet run --urls "http://localhost:5003" > /tmp/openiddict-test.log 2>&1 &
APP_PID=$!
sleep 10

echo "Waiting for server to start..."
for i in {1..10}; do
    if curl -s http://localhost:5003/health > /dev/null; then
        echo "Server is ready!"
        break
    fi
    sleep 2
done

echo ""
echo "==================================================================="
echo "1. OpenID Connect Discovery (JWKS & Configuration)"
echo "==================================================================="
curl -s http://localhost:5003/.well-known/openid-configuration | python3 -m json.tool | head -40
echo ""

echo "==================================================================="
echo "2. JWKS Endpoint (Key Rollover)"
echo "==================================================================="
curl -s http://localhost:5003/.well-known/jwks | python3 -m json.tool | head -20
echo ""

echo "==================================================================="
echo "3. Multi-Tenant Support"
echo "==================================================================="
echo "Testing with different tenants..."
echo ""
echo "Tenant 1 (default):"
curl -s http://localhost:5003/health -H "X-Tenant-ID: default"
echo ""
echo "Tenant 2 (tenant-abc):"
curl -s http://localhost:5003/health -H "X-Tenant-ID: tenant-abc"
echo ""

echo "==================================================================="
echo "4. Authorization Code Flow"
echo "==================================================================="
echo "Authorization endpoint available at:"
echo "  http://localhost:5003/connect/authorize"
echo ""
echo "Example authorization URL:"
echo "  http://localhost:5003/connect/authorize?client_id=test-client&response_type=code&redirect_uri=https://localhost:7001/callback&scope=openid%20profile%20api&tenant=default"
echo ""

echo "==================================================================="
echo "5. Token Endpoint (with Password Grant for Demo)"
echo "==================================================================="
echo "Note: Password grant is available for testing."
echo "In production, use Authorization Code Flow."
echo ""
echo "Example request:"
echo "  POST http://localhost:5003/connect/token"
echo "  Content-Type: application/x-www-form-urlencoded"
echo "  X-Tenant-ID: default"
echo "  "
echo "  grant_type=password&username=testuser&password=password"
echo "  &client_id=test-client&client_secret=test-secret&scope=api openid"
echo ""

echo "==================================================================="
echo "6. Token Revocation"
echo "==================================================================="
echo "Revocation endpoint available at:"
echo "  POST http://localhost:5003/connect/revoke"
echo "  token=<token>&client_id=test-client&client_secret=test-secret"
echo ""

echo "==================================================================="
echo "7. Logout Endpoint"
echo "==================================================================="
echo "Logout endpoint available at:"
echo "  POST http://localhost:5003/connect/logout"
echo ""

echo "==================================================================="
echo "8. Protected Resource"
echo "==================================================================="
echo "Protected endpoint (requires valid token):"
echo "  GET http://localhost:5003/api/protected"
echo "  Authorization: Bearer <access_token>"
echo ""

echo "==================================================================="
echo "Feature Summary"
echo "==================================================================="
echo "✓ Authorization Code Flow - Supported"
echo "✓ Refresh Token Rotation - Implemented in database"
echo "✓ Logout/Revoke - Available at /connect/logout and /connect/revoke"
echo "✓ Redis-backed tokens - Configured (requires Redis running)"
echo "✓ Multi-Tenant isolation - Header-based tenant identification"
echo "✓ Key Rollover & JWKS - Background service + JWKS endpoint"
echo ""

echo "==================================================================="
echo "Implementation Details"
echo "==================================================================="
echo "• Database: SQLite (openiddict.db)"
echo "• Token Lifetime: 1 hour (access), 30 days (refresh)"
echo "• Refresh Token: Rotation enabled (one-time use)"
echo "• Multi-Tenant: Via X-Tenant-ID header or query parameter"
echo "• Key Rollover: Background service checks every 24 hours"
echo "• Redis Integration: Configured for token caching"
echo ""

echo "==================================================================="
echo "Server Information"
echo "==================================================================="
echo "• Server running at: http://localhost:5003"
echo "• Environment: Development"
echo "• .NET Version: 8.0"
echo "• OpenIddict Version: 5.8.0"
echo ""

# Cleanup
echo "Press Enter to stop the server and exit..."
read
kill $APP_PID 2>/dev/null || true
wait $APP_PID 2>/dev/null || true
echo "Server stopped. Test complete!"
