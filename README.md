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

    // BC Edm.Date fields are date-only on the wire ("2026-10-28"), and unset dates come
    // back as "0001-01-01" — map them to DateOnly. A bare DateTimeOffset property fails
    // deserialization on EVERY row, populated or not, because neither form carries a time.
    public DateOnly PostingDate { get; set; }
}
```

> **Dates:** Business Central date fields are `Edm.Date` — always date-only, never
> timestamps. Model them as `DateOnly` (`System.Text.Json` reads both real dates and the
> `0001-01-01` unset sentinel natively). Datetime fields (`lastModifiedDateTime` and
> friends) are `Edm.DateTimeOffset` and map to `DateTimeOffset` as usual.

# 🔍 Querying

`Query<T>()` is the recommended entry point. Field names come from property selectors, so
they survive renames and always match how the entity is deserialized.

```csharp
var orders = await client.Query<SalesOrder>()
    .Where(f => f.Equals(o => o.Status, "Open")
                 .And(f.GreaterThan(o => o.Amount, 100)))
    .OrderByDescending(o => o.Amount)
    .ThenBy(o => o.No)
    .Top(50)
    .ToListAsync();
```

The `Where(f => ...)` builder infers the entity type from the query, so it is never
restated per operator; the static `Filter.Equals<SalesOrder>(...)` form remains available
and renders identically.

**`$select` is derived from the entity by default**: the query above sends
`$select=Sell_to_Customer_No,amount,no,status` — the settable scalar properties of
`SalesOrder`, resolved exactly like deserialization. The entity class states the
projection once; call sites stop restating it. An explicit `.Select(...)` narrows it,
`.SelectAll()` requests every column. Navigation properties and get-only computed
properties are excluded automatically.

> **A property with no matching column now fails the query.** Nothing in the package can
> consult your tenant's schema, so every derived name is discovered by the server. A
> property that maps to no column on the entity set used to bind as its default and cost
> nothing; it now enters `$select` and the whole request fails with a `400` before a row is
> read. The same applies to casing, since `$select` is **case-sensitive** on the server
> even though deserialization is not. Remedy either with `[JsonIgnore]` on the property or
> `.SelectAll()` on the query — the exception message names both. Watch for a shared base
> class of system fields inherited by entity sets that do not all expose them. Verifying
> your wire names against `$metadata` once, when adopting this, is cheaper than meeting
> them one `400` at a time.

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

Paging is **server-driven** (verified against a live BC SaaS tenant): by default no page
size is sent at all, Business Central pages at its own configured Max Page Size (20,000
online), and continuation follows `@odata.nextLink` — an opaque cursor immune to the
row-shift hazards of offset paging. To bound per-response size — memory, slow pages,
timeouts — request smaller pages; the value is sent as `Prefer: odata.maxpagesize` and
the server clamps it to its own maximum, so it can only ever ask for *less*:

```csharp
// per registration — the default for every streaming read
services.AddBusinessCentral(o => { /* ... */ o.MaxPageSize = 1000; });

// per query — overrides the registration value
await foreach (var order in client.Query<SalesOrder>().PageSize(500).StreamAsync())
{
    if (Process(order) is Done) break;   // no further pages are requested
}
```

`Top(n)` remains a pure result cap: it is sent as `$top` so the server never over-serves a
capped query, and enforced mid-page while continuations are followed.

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

// Single-entity reads. GetAsync returns null on 404 — "does it exist" is a question,
// not an error. Keys may be a systemId or an alternate key.
var one   = await client.GetAsync<SalesOrder>("salesOrders", "No='1000'");
var first = await client.FirstOrDefaultAsync<SalesOrder>("salesOrders", Filter.Equals("status", "Open"));

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
payload you sent is returned instead of throwing. (Measured live: `ODataV4` page endpoints
echo the entity on `PATCH` regardless of the header, so the `204` path is a safety net
rather than the common case.) Keys may be a `systemId` or an alternate
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
| `Filter.In` | `(field eq v1) or (field eq v2) ...` |
| `Filter.IsNull` | `field eq null` |
| `Filter.IsNotNull` | `field ne null` |

Combine with `.And(...)`, `.Or(...)` and `.Not()`:

```csharp
var filter = Filter.Equals<SalesOrder>(o => o.Status, "Open")
                   .And(Filter.GreaterThan<SalesOrder>(o => o.Amount, 100));
```

`Filter.In` renders a **same-field `or`-chain**, not the OData `in` operator — Business
Central rejects `in` without `$schemaversion=2.1` (`BadRequest_MethodNotImplemented`).
With an empty collection it yields a filter matching nothing, so passing an empty key set
is safe.

> **Business Central limitation:** `or` only works between filters on the *same* field.
> Combining filters on different fields with `.Or(...)` — `field1 eq 1 or field2 eq 2` —
> has no AL filter equivalent and the server rejects it. `.And(...)` has no such
> restriction.

> **Null means blank on text fields:** AL text fields cannot be null — an unset field is an
> empty string, and Business Central maps `eq null` onto "is blank". `Filter.IsNull` on a
> text field therefore matches empty strings, and `Filter.IsNotNull` *excludes* them —
> unlike the equivalent LINQ predicate. Verified against a live tenant.

## URL length and bulk key lookups

Because `Filter.In` renders an `or`-chain, a bulk key lookup grows the URL roughly **four
times faster** than the `in (...)` form suggests: `(no eq 'EBH100') or ` costs about 40
encoded characters per key where `'EBH100',` would cost 10. Past the server's limit
Business Central answers with a `400` or `404` that never mentions length.

The client measures and guards:

```csharp
services.AddBusinessCentral(o =>
{
    o.UrlLengthWarningThreshold = 2000;  // default — raises OnUrlLengthWarning
    o.MaxUrlLength              = 4000;  // default — throws ArgumentException
});
```

A URL between the two is **sent normally** and reported to your observer, so a deployment
can discover the length distribution its real workload produces and size chunking against
evidence. Past `MaxUrlLength` the client throws before the request leaves the process, with
a message naming the actual length, the limit, and the `or`-chain count when one is
present. Set either to `null` to disable it.

Server-issued `@odata.nextLink` continuations are never checked — the server produced them,
so its own limits already applied.

```csharp
public void OnUrlLengthWarning(BusinessCentralUrlLengthInfo url) =>
    _logger.LogWarning("BC URL {Length} chars ({OrClauses} or-clauses), limit {Limit}",
        url.Length, url.OrClauseCount, url.Limit);
```

## Field names without the builder

Path-based calls take field names as strings, which invites hand-maintained constants
classes that drift from the model. `BusinessCentralField.Of` resolves a selector exactly
the way deserialization does — `[JsonPropertyName]` first, then the camelCase policy — so
wire names live in one place, the entity. `EntityPath.For<T>()` does the same for the
entity set path:

```csharp
var lines = await client.QueryAsync<ProdOrderLine>(
    EntityPath.For<ProdOrderLine>(),                       // path from [BusinessCentralEntity]
    Filter.Equals<ProdOrderLine>(l => l.OrderNo, orderNo), // typed filters resolve the same way
    select: [BusinessCentralField.Of<ProdOrderLine>(l => l.ItemNo),
             BusinessCentralField.Of<ProdOrderLine>(l => l.Quantity)]);
```

# ♻️ Throttling and retries

Business Central throttles aggressively. Throttled (`429`) and transient (`408`, `502`,
`503`, `504`) responses are retried automatically, honouring `Retry-After` when present.
Delays are jittered (`Retry.JitterFactor`, default `0.2`) so concurrent callers do not
retry in lockstep — the spread is only ever added, never subtracted from a `Retry-After`.
Token acquisition follows the same retry options; bad credentials are not retried.

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
| `PATCH` | retried | retried (see below) |
| `POST` | retried | **not** retried; the exception is raised |

`PATCH` is replayed too. RFC 9110 does not guarantee PATCH is idempotent, but this client
only ever sends a JSON merge of absolute field values, so applying it twice converges on the
same state. If your payload carries relative operations, disable retries or pass a real
`If-Match` ETag instead of `*`.

Without this, a `504` on a `POST` could duplicate a record that Business Central had already
created. If your endpoint deduplicates server-side, or duplicates are acceptable, opt back
in with `options.Retry.RetryPostOnTransientFailures = true`.

Connection failures and client-side timeouts — no response received at all — are just as
ambiguous as a `504` and follow the same column: idempotent methods are retried, `POST` is
not. They surface as `BusinessCentralConnectionException`.

## Composing with an existing resilience pipeline

If a global handler wraps every `HttpClient` — e.g. .NET Aspire's
`ConfigureHttpClientDefaults` with `AddStandardResilienceHandler` — the outer retry and
this package's retry compose multiplicatively. Worse, the standard handler replays `POST`
on ambiguous failures, which this package deliberately refuses to do; with both active,
the outer handler retries before the package ever sees the failure.

Prefer exempting the package's clients and keeping the built-in retry, which honours
`Retry-After` and knows which requests are safe to replay. Both clients are addressable
by name:

```csharp
services.AddHttpClient(BusinessCentralHttpClients.Client).RemoveAllResilienceHandlers();
services.AddHttpClient(BusinessCentralHttpClients.Token).RemoveAllResilienceHandlers();
```

Exempting the token client is safe: token acquisition has its own retry under the same
`Retry` options, so removing the outer handler does not leave it bare. The per-attempt
timeout of the data client is configurable via `options.RequestTimeout` (default: the
`HttpClient` 100s) — budget `Retry.MaxAttempts × (RequestTimeout + backoff)` against any
outer execution timeout.

## Mapping exceptions in message-level retry policies

If a message bus or job runner (Wolverine, MassTransit, Hangfire, Polly) retries around
client calls, key its policies on this package's exception types — **not** on
`HttpRequestException`, which the client never lets escape:

| Match | Meaning | Suggested policy |
| ----- | ------- | ---------------- |
| `BusinessCentralConnectionException` | no response — connection failure / timeout (already retried in-process) | fast requeue curve |
| `BusinessCentralThrottledException` | `429` — the client already honoured `Retry-After` | slow requeue curve |
| `ex.IsTransient` | any retry-worthy failure | generic transient handling |
| everything else | validation/auth/not-found — retrying cannot help | dead-letter / alert |

A policy keyed on `HttpRequestException` written for 1.x **silently stops matching** on
2.0 — transport failures surface as `BusinessCentralConnectionException` with the original
exception as `InnerException`.

Disabling the package's retry instead (`options.Retry.Enabled = false`) also resolves the
composition, but leaves the generic outer handler in charge — including its unsafe `POST`
replay.

# ⚠️ Errors

All failures derive from `BusinessCentralException`. `Message` is a single line suitable
for logging; the detail lives on properties, and `ToString()` renders everything.

| Type | When |
| ---- | ---- |
| `BusinessCentralValidationException` | `400` |
| `BusinessCentralAuthException` | `401`, `403` |
| `BusinessCentralNotFoundException` | `404` |
| `BusinessCentralThrottledException` | `429` |
| `BusinessCentralConnectionException` | no response — connection failure or client-side timeout; `StatusCode` is `0` |
| `BusinessCentralServerException` | everything else, and deserialization failures |

```csharp
catch (BusinessCentralException ex)
{
    logger.LogError(ex, "BC call failed: {Code} {CorrelationId}",
        ex.ODataErrorCode, ex.CorrelationId);

    if (ex.IsTransient) { /* safe to try again */ }
}
```

The subtypes are sealed **siblings**, not a hierarchy — a guard like
`catch (BusinessCentralServerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)`
compiles but can never match, because a `404` is a `BusinessCentralNotFoundException`.
Prefer the predicates on the base type, which make the safe form the obvious one:

```csharp
catch (BusinessCentralException ex) when (ex.IsNotFound)
{
    // already gone — treat the delete as idempotent success
}
```

`IsNotFound`, `IsThrottled`, `IsValidation`, `IsAuth`, `IsConnectionFailure` and
`IsTransient` cover the same distinctions as the subtypes, without the trap.

# 📊 Diagnostics

There is no logging dependency. Implement `IBusinessCentralObserver` to hook requests,
retries and the token lifecycle:

```csharp
services.AddObserver<MyObserver>();
```

Every member except the core request callbacks has a default implementation, so you only
override what you care about.

# 🧰 Testing

The companion package [`Dynamics365.BusinessCentral.Testing`](https://www.nuget.org/packages/Dynamics365.BusinessCentral.Testing)
runs a **real** client over a scripted transport, so tests exercise URL building, filter
rendering, paging, retry and deserialization — and can assert the exact OData a call
produced, which a mock of `IBusinessCentralClient` never can:

```csharp
using var bc = new FakeBusinessCentral();
bc.EnqueuePage(new Item { No = "X", Description = "Pump" });

var items = await bc.Client.QueryAsync<Item>("items",
    Filter.Equals("no", "X"), select: ["no", "description"]);

Assert.Equal(
    "/Company('TEST')/items?$filter=no eq 'X'&$select=no,description",
    bc.Requests.Single().DecodedPathAndQuery);
```

Multi-page responses (`EnqueuePage(..., nextLink: "page2")`), failures by status code
(`EnqueueError(HttpStatusCode.TooManyRequests, retryAfter: …)` raises the matching
exception subtype) and network failures are all scriptable; token acquisition is answered
automatically. For stateful fakes, every `IBusinessCentralClient` member has a default
implementation, so a hand-written fake implements only the members it uses.
