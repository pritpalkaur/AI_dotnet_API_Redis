# Compares response times: SQL Server (cache MISS) vs Redis (cache HIT).
#   .\scripts\benchmark.ps1                      # against the Docker API on :8080
#   .\scripts\benchmark.ps1 -BaseUrl http://localhost:5170 -Iterations 50
param(
    [string]$BaseUrl = "http://localhost:8080",
    [int]$Iterations = 20
)

$scenarios = [ordered]@{
    "Single product by id"           = "/api/products/100000"
    "List page 1 (all products)"     = "/api/products?page=1&pageSize=20"
    "List by category"               = "/api/products?category=Laptops&page=1&pageSize=20"
    "Deep page (page 5000)"          = "/api/products?page=5000&pageSize=20"
    "Category + deep page (500)"     = "/api/products?category=Laptops&page=500&pageSize=20"
}

function Measure-Request([string]$url) {
    # curl prints: <X-Cache header value> <seconds>
    $out = curl.exe -s -o NUL -w "%header{x-cache} %{time_total}" $url
    $cache, $secs = $out -split ' '
    [pscustomobject]@{ Cache = $cache; Ms = [double]$secs * 1000 }
}

function Stats($samples) {
    $sorted = $samples | Sort-Object
    [pscustomobject]@{
        Avg    = ($samples | Measure-Object -Average).Average
        Median = $sorted[[int][math]::Floor($sorted.Count / 2)]
    }
}

# Warm up the API (JIT, DB connection pool, Redis connection) so the first scenario isn't penalised.
1..3 | ForEach-Object { Measure-Request "$BaseUrl/api/products/1" | Out-Null }

$results = foreach ($name in $scenarios.Keys) {
    $url = $BaseUrl + $scenarios[$name]
    Write-Host "Running: $name" -ForegroundColor Cyan

    # MISS: empty Redis before every request so the API must query SQL Server.
    $miss = foreach ($i in 1..$Iterations) {
        docker compose exec -T redis redis-cli FLUSHALL | Out-Null
        $r = Measure-Request $url
        if ($r.Cache -ne "MISS") { Write-Warning "expected MISS, got '$($r.Cache)'" }
        $r.Ms
    }

    # HIT: one request fills the cache, then every request is served from Redis.
    Measure-Request $url | Out-Null
    $hit = foreach ($i in 1..$Iterations) {
        $r = Measure-Request $url
        if ($r.Cache -ne "HIT") { Write-Warning "expected HIT, got '$($r.Cache)'" }
        $r.Ms
    }

    $m = Stats $miss; $h = Stats $hit
    [pscustomobject]@{
        Scenario            = $name
        "SQL median (ms)"   = [math]::Round($m.Median, 1)
        "Redis median (ms)" = [math]::Round($h.Median, 1)
        "SQL avg (ms)"      = [math]::Round($m.Avg, 1)
        "Redis avg (ms)"    = [math]::Round($h.Avg, 1)
        Faster              = "{0:N1}x" -f ($m.Median / $h.Median)
    }
}

$results | Format-Table -AutoSize
