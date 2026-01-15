<div align="center">
  <h1>Dynamics365.BusinessCentral</h1>
  <p>A lightweight, strongly-typed client for the Dynamics 365 Business Central OData API.</p>
</div>

<b>✨ Features</b>

- Typed OData querying with fluent filter composition
- Built-in OAuth2 client credentials authentication
- Automatic token caching and refresh
- Fluent paging, ordering and selection helpers
- Clean DI integration
- No runtime dependencies beyond HttpClient and System.Text.Json

<b>📦 Installation</b>

```bash
dotnet add package Dynamics365.BusinessCentral
```

<b>🧩 Dependency Injection</b>

```csharp
services.AddBusinessCentral(options =>
{
    options.TenantId = "your-tenant-id";
    options.ClientId = "your-client-id";
    options.ClientSecret = "your-client-secret";
    options.Company = "CRONUS AG";
    options.BaseUrl = "https://api.businesscentral.dynamics.com/v2.0/{tenant}/Production/ODataV4";
    options.TokenEndpoint = "https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token";
    options.Scope = "https://api.businesscentral.dynamics.com/.default";
});

public class MyService
{
    private readonly IBusinessCentralClient _client;

    public MyService(IBusinessCentralClient client)
    {
        _client = client;
    }
}
```

<b>🔍 Querying</b>

Simple Query
```csharp
var orders = await client.QueryAsync<Order>("salesOrders");
```

With Filters
```csharp
var filter =
    Filter.Equals("Status", "Open")
          .And(Filter.GreaterThan("Amount", 100));

var orders = await client.QueryAsync<Order>("salesOrders", filter);
```

Paging and Ordering
```csharp
var orders = await client.QueryAsync<Order>(
    "salesOrders",
    Filter.Equals("Status", "Open"),
    o => o.WithTop(100).OrderByAsc("No"));
```

Query All
```csharp
var allOrders = await client.QueryAllAsync<Order>("salesOrders");
```

Raw Query
```csharp
var raw = await client.QueryRawAsync<JsonElement>("salesOrders?$top=5");
```

Patch
```csharp
await client.PatchAsync(
    "salesOrders",
    "No='1000'",
    new { Status = "Released" });
```

<b>🧪 Filters</b>

| Method                  | Expression                |
| ----------------------- | ------------------------- |
| `Filter.Equals`         | `field eq value`          |
| `Filter.NotEquals`      | `field ne value`          |
| `Filter.GreaterThan`    | `field gt value`          |
| `Filter.GreaterOrEqual` | `field ge value`          |
| `Filter.LessThan`       | `field lt value`          |
| `Filter.LessOrEqual`    | `field le value`          |
| `Filter.Contains`       | `contains(field,value)`   |
| `Filter.StartsWith`     | `startswith(field,value)` |
| `Filter.EndsWith`       | `endswith(field,value)`   |
| `Filter.In`             | `field in (...)`          |
| `Filter.IsNull`         | `field eq null`           |
| `Filter.IsNotNull`      | `field ne null`           |

