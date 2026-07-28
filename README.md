# Dynamics365.BusinessCentral
*A lightweight, strongly-typed client for the Dynamics 365 Business Central OData API.*

# ✨ Features

- Strongly-typed queries — field names come from your entity, not from strings
- Built-in OAuth2 client credentials authentication, with a shared token cache
- Automatic retry of throttled (`429`) and transient failures, honouring `Retry-After`
- Streaming and automatic paging, including server-driven `@odata.nextLink`
- Filtering, ordering, projection, expansion and counting
- Multi-company support from a single registration
- Clean DI integration
- No runtime dependencies beyond `HttpClient` and `System.Text.Json`

Upgrading from 1.x? See [MIGRATION.md](MIGRATION.md). Full history in [CHANGELOG.md](CHANGELOG.md).

# 📦 Installation

```bash
dotnet add package Dynamics365.BusinessCentral
```

# 🧩 Setup

Only four settings are required. `BaseUrl` and `TokenEndpoint` default to the Business
Central SaaS endpoints and understand the `{tenant}` and `{environment}` placeholders.

```csharp
services.AddBusinessCentral(options =>
{
    options.TenantId     = "your-tenant-id";
    options.ClientId     = "your-client-id";
    options.ClientSecret = "your-client-secret";
    options.Company      = "CRONUS AG";

    // Optional — defaults to "Production"
    options.Environment  = "Sandbox";
});
```

Or bind from configuration:

```csharp
services.AddBusinessCentral(builder.Configuration.GetSection("BusinessCentral"));
```

```json
{
  "BusinessCentral": {
    "TenantId": "...",
    "ClientId": "...",
    "ClientSecret": "...",
    "Company": "CRONUS AG",
    "Environment": "Production",
    "Retry": { "MaxAttempts": 3 }
  }
}
```

Then inject `IBusinessCentralClient`:

```csharp
public class MyService(IBusinessCentralClient client)
{
    public Task<List<SalesOrder>> GetOpenOrders() =>
        client.Query<SalesOrder>()
              .Where(Filter.Equals<SalesOrder>(o => o.Status, "Open"))
              .ToListAsync();
}
```

Point an entity at its OData entity set once, and stop repeating the path:

```csharp
[BusinessCentralEntity("salesOrders")]
public sealed class SalesOrder
{
    public string No { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
}
```

# 🔍 Querying

`Query<T>()` is the recommended entry point. Field names come from property selectors, so
they survive renames and always match how the entity is deserialized.

```csharp
var orders = await client.Query<SalesOrder>()
    .Where(Filter.Equals<SalesOrder>(o => o.Status, "Open"))
    .Where(Filter.GreaterThan<SalesOrder>(o => o.Amount, 100))
    .OrderByDescending(o => o.Amount)
    .ThenBy(o => o.No)
    .Select(o => o.No, o => o.Amount)
    .Top(50)
    .ToListAsync();
```

| Operation | Method |
| --------- | ------ |
| One page | `ToListAsync()` |
| Everything, auto-paged | `ToAllAsync()` |
| Everything, lazily | `StreamAsync()` |
| Page plus total | `ToPageAsync()` |
| Single row | `FirstOrDefaultAsync()` |
| Count only | `CountAsync()` |

## Streaming

`StreamAsync` fetches pages as you consume them and stops fetching when you stop reading —
prefer it over `ToAllAsync` for large sets.

```csharp
await foreach (var order in client.Query<SalesOrder>().PageSize(500).StreamAsync())
{
    if (Process(order) is Done) break;   // no further pages are requested
}
```

## Counting and paging

```csharp
var page = await client.Query<SalesOrder>().Top(50).ToPageAsync();

Console.WriteLine($"{page.Items.Count} of {page.TotalCount}");

var total = await client.Query<SalesOrder>()
    .Where(Filter.Equals<SalesOrder>(o => o.Status, "Open"))
    .CountAsync();
```

## Expanding

```csharp
var orders = await client.Query<SalesOrder>()
    .Expand(o => o.Lines)
    .ToListAsync();

// Raw OData expand syntax also works
var withNested = await client.Query<SalesOrder>()
    .Expand("salesOrderLines($select=lineNo,amount)")
    .ToListAsync();
```

## Path-based access

The lower-level API remains available when you do not want an annotated type.

```csharp
var orders = await client.QueryAsync<SalesOrder>("salesOrders", Filter.Equals("status", "Open"));
var all    = await client.QueryAllAsync<SalesOrder>("salesOrders");
var raw    = await client.QueryRawAsync<JsonElement>("salesOrders?$top=5");

await foreach (var o in client.QueryStreamAsync<SalesOrder>("salesOrders")) { }
```

# ✏️ Writing

```csharp
await client.PostAsync("salesOrders", new SalesOrder { No = "1000" });

await client.PatchAsync("salesOrders", "No='1000'", new { Status = "Released" });

await client.PutAsync("salesOrders", systemId, order, ifMatch: etag);

await client.DeleteAsync("salesOrders", systemId);
```

Writes send `Prefer: return=representation`. If the server answers `204 No Content`, the
payload you sent is returned instead of throwing. Keys may be a `systemId` or an alternate
key such as `No='1000'`.

When the response type differs from the payload — posting an anonymous object and reading
back an entity — use the two-generic overloads instead of `dynamic`:

```csharp
var created = await client.PostAsync<object, CreatedRow>(
    "ldatSummary",
    new { serialNo = "S1", productionOrderNo = "PO1" });

// null means BC applied the write but returned no representation. Failures throw.
if (created is null) { /* handle "created but not echoed" */ }
```

`TResult` is unconstrained, so `JsonElement` works when you have no model:

```csharp
var element = await client.PostAsync<object, JsonElement>("ldatSummary", payload);
```

# 🏢 Multiple companies

One registration serves every company in the tenant. `ForCompany` shares the underlying
HTTP client and token cache, so it costs nothing.

```csharp
foreach (var company in await client.GetCompaniesAsync())
{
    var scoped = client.ForCompany(company.Name);
    var orders = await scoped.Query<SalesOrder>().ToAllAsync();
}
```

# 🧪 Filters

Every method has a string overload and a typed overload.

| Method | Expression |
| ------ | ---------- |
| `Filter.Equals` | `field eq value` |
| `Filter.NotEquals` | `field ne value` |
| `Filter.GreaterThan` | `field gt value` |
| `Filter.GreaterOrEqual` | `field ge value` |
| `Filter.LessThan` | `field lt value` |
| `Filter.LessOrEqual` | `field le value` |
| `Filter.Contains` | `contains(field,value)` |
| `Filter.StartsWith` | `startswith(field,value)` |
| `Filter.EndsWith` | `endswith(field,value)` |
| `Filter.In` | `field in (...)` |
| `Filter.IsNull` | `field eq null` |
| `Filter.IsNotNull` | `field ne null` |

Combine with `.And(...)`, `.Or(...)` and `.Not()`:

```csharp
var filter = Filter.Equals<SalesOrder>(o => o.Status, "Open")
                   .And(Filter.GreaterThan<SalesOrder>(o => o.Amount, 100));
```

`Filter.In` with an empty collection yields a filter matching nothing, rather than the
invalid OData expression `field in ()`.

# ♻️ Throttling and retries

Business Central throttles aggressively. Throttled (`429`) and transient (`408`, `502`,
`503`, `504`) responses are retried automatically, honouring `Retry-After` when present.

```csharp
services.AddBusinessCentral(options =>
{
    options.Retry.MaxAttempts = 5;
    options.Retry.BaseDelay   = TimeSpan.FromSeconds(2);
    options.Retry.MaxDelay    = TimeSpan.FromSeconds(30);
    // options.Retry.Enabled  = false;   // surface transient failures immediately
});
```

**Writes are not blindly replayed.** A `429` is rejected before Business Central processes
it, so replaying is always safe. The other transient statuses are ambiguous — the write may
already have been applied — so:

| Method | `429` | `408` / `502` / `503` / `504` |
| ------ | ----- | ----------------------------- |
| `GET`, `PUT`, `DELETE` | retried | retried (idempotent — replay converges) |
| `POST` | retried | **not** retried; the exception is raised |

Without this, a `504` on a `POST` could duplicate a record that Business Central had already
created. If your endpoint deduplicates server-side, or duplicates are acceptable, opt back
in with `options.Retry.RetryPostOnTransientFailures = true`.

# ⚠️ Errors

All failures derive from `BusinessCentralException`. `Message` is a single line suitable
for logging; the detail lives on properties, and `ToString()` renders everything.

| Type | When |
| ---- | ---- |
| `BusinessCentralValidationException` | `400` |
| `BusinessCentralAuthException` | `401`, `403` |
| `BusinessCentralNotFoundException` | `404` |
| `BusinessCentralThrottledException` | `429` |
| `BusinessCentralServerException` | everything else, and deserialization failures |

```csharp
catch (BusinessCentralException ex)
{
    logger.LogError(ex, "BC call failed: {Code} {CorrelationId}",
        ex.ODataErrorCode, ex.CorrelationId);

    if (ex.IsTransient) { /* safe to try again */ }
}
```

# 📊 Diagnostics

There is no logging dependency. Implement `IBusinessCentralObserver` to hook requests,
retries and the token lifecycle:

```csharp
services.AddObserver<MyObserver>();
```

Every member except the core request callbacks has a default implementation, so you only
override what you care about.
