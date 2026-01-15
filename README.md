<div align="center">
  <h1>Dynamics365.BusinessCentral</h1>
  <p>A lightweight, strongly-typed client for the Dynamics 365 Business Central OData API.</p>
</div>

✨ Features

- Typed OData querying with fluent filter composition
- Built-in OAuth2 client credentials authentication
- Automatic token caching and refresh
- Fluent paging, ordering and selection helpers
- Clean DI integration
- No runtime dependencies beyond HttpClient and System.Text.Json

📦 Installation

```bash
dotnet add package Dynamics365.BusinessCentral
```

🧩 Dependency Injection

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
