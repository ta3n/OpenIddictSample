# 🔄 Migration từ SQL Server sang PostgreSQL

## ✅ Đã Hoàn Thành

Project đã được chuyển từ SQL Server sang PostgreSQL với các thay đổi sau:

### 📦 Package Changes

**Removed:**

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.0" />
```

**Added:**

```xml
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.0" />
```

### 🔧 Configuration Changes

**appsettings.json:**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=OpenIddictSample;Username=postgres;Password=YourPassword123!"
  }
}
```

**Program.cs:**

```csharp
// Before
options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));

// After
options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
```

### 🐳 Docker Compose Changes

**compose.yaml:**

- Replaced SQL Server container với PostgreSQL
- Port: `1433` → `5432`
- Container name: `openiddict_sqlserver` → `openiddict_postgres`
- Volume: `sqlserver_data` → `postgres_data`

## 🚀 Quick Start với PostgreSQL

### 1. Start PostgreSQL và Redis

```bash
docker-compose up -d postgres redis
```

Hoặc cài đặt trực tiếp:

**macOS (Homebrew):**

```bash
brew install postgresql@16
brew services start postgresql@16
```

**Ubuntu/Debian:**

```bash
sudo apt-get install postgresql-16
sudo systemctl start postgresql
```

**Windows:**
Download và cài đặt từ: https://www.postgresql.org/download/windows/

### 2. Verify PostgreSQL đang chạy

```bash
# Check container
docker ps | grep postgres

# Test connection
psql -h localhost -U postgres -d postgres
# Password: YourPassword123!

# Hoặc với Docker
docker exec -it openiddict_postgres psql -U postgres
```

### 3. Chạy Application

```bash
dotnet run
```

Database sẽ tự động được tạo khi app khởi động.

## 🔍 PostgreSQL vs SQL Server

### Connection String Format

**SQL Server:**

```
Server=localhost;Database=MyDb;User Id=sa;Password=Pass123!;TrustServerCertificate=True
```

**PostgreSQL:**

```
Host=localhost;Port=5432;Database=MyDb;Username=postgres;Password=Pass123!
```

### Port Numbers

| Database   | Default Port |
|------------|--------------|
| SQL Server | 1433         |
| PostgreSQL | 5432         |

### Data Type Differences

OpenIddict và EF Core tự động handle các differences, nhưng lưu ý:

| SQL Server       | PostgreSQL |
|------------------|------------|
| NVARCHAR(MAX)    | TEXT       |
| DATETIME2        | TIMESTAMP  |
| UNIQUEIDENTIFIER | UUID       |
| BIT              | BOOLEAN    |

## 📊 Database Management Tools

### pgAdmin (GUI)

```bash
# Via Docker
docker run -d \
  --name pgadmin \
  -p 5050:80 \
  -e PGADMIN_DEFAULT_EMAIL=admin@admin.com \
  -e PGADMIN_DEFAULT_PASSWORD=admin \
  dpage/pgadmin4
```

Mở: http://localhost:5050

### Command Line Tools

**List databases:**

```bash
docker exec -it openiddict_postgres psql -U postgres -c "\l"
```

**Connect to database:**

```bash
docker exec -it openiddict_postgres psql -U postgres -d OpenIddictSample
```

**Common psql commands:**

```sql
\dt              -- List tables
\d table_name    -- Describe table
\q               -- Quit
```

## 🔄 Migration Scripts (If Needed)

Nếu bạn cần migrate data từ SQL Server sang PostgreSQL:

### Using EF Core Migrations

```bash
# Create migration
dotnet ef migrations add InitialPostgreSQL

# Apply migration
dotnet ef database update
```

### Manual Data Export/Import

**Export from SQL Server:**

```bash
bcp OpenIddictSample.dbo.Users out users.csv -c -t, -S localhost -U sa -P YourPass
```

**Import to PostgreSQL:**

```bash
psql -U postgres -d OpenIddictSample -c "\COPY Users FROM 'users.csv' CSV"
```

## 🛠️ Troubleshooting

### Error: "password authentication failed"

Check password trong connection string và Docker environment variables phải match.

### Error: "database does not exist"

Application sẽ tự động tạo database với `EnsureCreatedAsync()`. Hoặc tạo manual:

```bash
docker exec -it openiddict_postgres psql -U postgres -c "CREATE DATABASE OpenIddictSample;"
```

### Error: "could not connect to server"

```bash
# Check PostgreSQL is running
docker ps | grep postgres

# Check logs
docker logs openiddict_postgres

# Restart if needed
docker restart openiddict_postgres
```

### Permission Issues

```bash
# Grant all privileges
docker exec -it openiddict_postgres psql -U postgres -c "GRANT ALL PRIVILEGES ON DATABASE OpenIddictSample TO postgres;"
```

## 📈 Performance Tips

### 1. Connection Pooling

PostgreSQL connection pooling được enable mặc định với Npgsql.

**Customize pooling:**

```
Host=localhost;Port=5432;Database=OpenIddictSample;Username=postgres;Password=Pass123!;Minimum Pool Size=5;Maximum Pool Size=100
```

### 2. Indexes

PostgreSQL tự động tạo indexes cho primary keys và unique constraints. OpenIddict đã configure các indexes cần thiết.

### 3. Monitor Performance

```sql
-- Active connections
SELECT * FROM pg_stat_activity WHERE datname = 'OpenIddictSample';

-- Table sizes
SELECT
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size
FROM pg_tables
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;
```

## 🔐 Security Best Practices

### 1. Change Default Password

```bash
docker exec -it openiddict_postgres psql -U postgres -c "ALTER USER postgres PASSWORD 'NewStrongPassword123!';"
```

Cập nhật trong `appsettings.json` và `compose.yaml`.

### 2. Use Environment Variables

**appsettings.Production.json:**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=${DB_HOST};Port=${DB_PORT};Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD}"
  }
}
```

### 3. SSL/TLS Connection

**For production:**

```
Host=prod.postgres.com;Port=5432;Database=OpenIddictSample;Username=postgres;Password=Pass123!;SSL Mode=Require;Trust Server Certificate=true
```

## 📚 Resources

- [Npgsql Documentation](https://www.npgsql.org/doc/)
- [PostgreSQL Documentation](https://www.postgresql.org/docs/)
- [EF Core with PostgreSQL](https://www.npgsql.org/efcore/)

---

**Note:** Tất cả functionality (Authorization Code Flow, Refresh Token Rotation, Multi-Tenant, BFF, etc.) vẫn hoạt động giống hệt với
PostgreSQL!
