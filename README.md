# Product API (.NET 8 + SQL Server + Redis)

A stateless CRUD REST API for products, with Redis cache-aside in front of SQL Server, plus a console client that reads and summarizes the data.

```
src/ProductApi      ASP.NET Core 8 Web API (EF Core / SQL Server, Redis via IDistributedCache)
src/ProductClient   Console client: summary report, list, get, add
db/schema.sql       Table DDL (the API auto-creates it in Development)
docker-compose.yml  Redis (and optionally SQL Server) for local dev
```

## Run with Docker

```
copy .env.example .env      # then fill in SQL_USER / SQL_PASSWORD (.env is git-ignored)
docker compose up -d --build
```

- API + Swagger: http://localhost:8080/swagger
- Redis web UI: http://localhost:8081 (watch `ProductApi:*` keys appear as you call the API)
- Client: `$env:PRODUCT_API_URL="http://localhost:8080"; dotnet run --project src/ProductClient`

The API container uses the SQL Server on your machine via `host.docker.internal:1433`.

## Run locally

1. **SQL Server**: `appsettings.json` defaults to a local instance with Windows auth. To use SQL
   authentication, keep the credentials out of git with user-secrets:
   `dotnet user-secrets set "ConnectionStrings:SqlServer" "Server=localhost;Database=ProductDb;User Id=<user>;Password=<password>;TrustServerCertificate=True" --project src/ProductApi`.
   In Development the database and table are created on startup. Otherwise run `db/schema.sql`.
2. **Redis**: `docker compose up -d redis`. Redis is optional: if it's unreachable the API logs a
   warning and serves straight from SQL Server.
3. **API**: `dotnet run --project src/ProductApi --launch-profile http`, then open
   http://localhost:5170/swagger.
4. **Client**:
   ```
   dotnet run --project src/ProductClient                                   # summary report
   dotnet run --project src/ProductClient -- list [category]
   dotnet run --project src/ProductClient -- get 1
   dotnet run --project src/ProductClient -- add KB-001 "Keyboard" 89.99 25 Peripherals
   ```

Connection strings live in `src/ProductApi/appsettings.json` and can be overridden with environment
variables, e.g. `ConnectionStrings__SqlServer` and `ConnectionStrings__Redis`.

## Endpoints

| Method | Route                                           | Result                          |
|--------|-------------------------------------------------|---------------------------------|
| POST   | `/api/products`                                 | 201 + Location, 400, 409 (dup SKU) |
| GET    | `/api/products?page=1&pageSize=20&category=...` | 200 paged list                  |
| GET    | `/api/products/{id}`                            | 200 / 404                       |
| PUT    | `/api/products/{id}`                            | 200 / 400 / 404 (SKU is immutable) |
| DELETE | `/api/products/{id}`                            | 204 / 404                       |
| GET    | `/health`                                       | SQL Server health check         |

Insert example:

```json
POST /api/products
{ "sku": "KB-001", "name": "Mechanical Keyboard", "category": "Peripherals", "price": 89.99, "quantity": 25 }
```

`src/ProductApi/ProductApi.http` has every request ready to run from VS / VS Code.

## Measure the cache benefit

```
sqlcmd -S localhost -U <user> -P <password> -d ProductDb -C -i scripts\seed.sql -v Rows=200000
.\scripts\benchmark.ps1
```

`seed.sql` bulk-inserts test products (re-runnable). `benchmark.ps1` times each endpoint with Redis
flushed before every call (SQL Server) and with a warm cache (Redis).

## How caching and statelessness work

- **Stateless**: no sessions or in-process data. SQL Server is the source of truth and Redis is a
  shared cache, so you can run several API instances behind a load balancer and any one can serve
  any request.
- **Cache-aside**: reads check Redis first (`product:{id}`, 10 min TTL; list pages, 2 min TTL), fall
  back to SQL, then populate Redis. Responses carry `X-Cache: HIT|MISS` so you can watch it work.
- **Invalidation**: writes remove `product:{id}` and rotate a `products:list:version` token that's
  part of every list-page key, so all cached list pages go stale at once across all instances.
- **Resilience**: a Redis error never fails a request; the cache is bypassed for 30 s and the API
  keeps serving from SQL.
