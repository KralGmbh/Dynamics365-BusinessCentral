# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`Dynamics365.BusinessCentral` — a NuGet library: a lightweight, strongly-typed client for the Dynamics 365 Business Central OData v4 API. Three runtime dependencies (`Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options.ConfigurationExtensions`); everything else is `HttpClient` + `System.Text.Json`. Staying near-dependency-free is a deliberate design goal — don't add packages casually.

Package references are declared **per TFM** (8.0.x / 9.0.x / 10.0.x) in both csproj files. Adding a package to one project means adding all three conditional entries to both, or restore fails with NU1605 downgrade errors.

## Commands

The solution uses the newer `.slnx` format, so a recent SDK is required (CI uses 10.0.x).

```bash
dotnet build Dynamics365.BusinessCentral.slnx
dotnet test  Dynamics365.BusinessCentral.slnx

# Both projects multi-target net8.0/net9.0/net10.0 — a bare `dotnet test` runs
# the suite once per TFM. Pin one while iterating. CI uses net8.0, but running
# that locally needs the .NET 8 runtime installed; with only the .NET 10 SDK
# present, `-f net8.0` fails at test-host launch. Use net10.0 locally:
dotnet test Dynamics365.BusinessCentral.slnx -f net10.0

# Single test / class (xUnit v2 filter syntax):
dotnet test -f net10.0 --filter "FullyQualifiedName~ClientTests.QueryAll_Pages_Until_Short_Page"
dotnet test -f net10.0 --filter "FullyQualifiedName~ObserverTests"

dotnet pack -c Release          # produces .nupkg + .snupkg
```

CI: `.github/workflows/sonar.yml` builds + tests on net8.0 and runs SonarCloud on every push/PR to `master`. `.github/workflows/nuget.yml` packs and pushes to NuGet on published GitHub releases — bump `<Version>` in the csproj before tagging.

## Architecture

`src/Dynamics365.BusinessCentral/` — one assembly, folders by concern:

- **`Client/`** — `BusinessCentralClient` (sealed, the only implementation of `IBusinessCentralClient`) plus the internal `BusinessCentralTokenProvider`, `BusinessCentralUrlBuilder` and `HttpRequestExtensions`.
- **`OData/`** — the typed query surface: `IBusinessCentralQuery<T>`/`BusinessCentralQuery<T>` (fluent builder), `PropertyPath` (selector → field name), `EntityPath` + `BusinessCentralEntityAttribute` (type → entity set), `Filter`/`ODataFilter`/`FilterExtensions`, `QueryOptions`, `ODataResponse<T>`, `BusinessCentralPage<T>`, `BusinessCentralCompany`.
- **`Options/`** — `BusinessCentralOptions` (credentials, base URL, company), `BusinessCentralOptionsValidator`, and `BusinessCentralJson.Options`, the single shared `JsonSerializerOptions` (camelCase policy, case-insensitive read) used by both the client and the exception factory.
- **`Errors/`** — `BusinessCentralException` base + five sealed subtypes (incl. `BusinessCentralThrottledException`); `BusinessCentralExceptionFactory` maps status codes and parses `Retry-After`.
- **`Diagnostics/`** — `IBusinessCentralObserver` and its info DTOs.
- **`ServiceCollectionExtensions.cs`** — `AddBusinessCentral` / `AddObserver<T>`.

### Request pipeline

Everything funnels through `BusinessCentralClient.SendWithAuthRetryAsync`, a single loop with **two independent retry budgets**:

- **auth** — one retry, tracked by `authRetried`. A `401` invalidates the token and retries once.
- **transient** — `BusinessCentralRetryOptions.MaxAttempts`, tracked by `transientAttempt`. Gated by `IsSafeToReplay`, not by `IsTransient` alone. `ComputeDelay` prefers the server's `Retry-After`, else doubles `BaseDelay`; both capped by `MaxDelay`.

`IsSafeToReplay` encodes a deliberate asymmetry: a `429` is rejected *before* processing so it is always replayable, but `408/502/503/504` are ambiguous — the write may already have landed. A `POST` is therefore not replayed on those unless `Retry.RetryPostOnTransientFailures` is set, because a duplicate row is worse than a surfaced error. `GET`/`PUT`/`DELETE` are idempotent and always retried. Don't "simplify" this back to a plain `IsTransient` check.

Each attempt clones the request (`HttpRequestExtensions.Clone`, because a sent `HttpRequestMessage` can't be reused). A failure is reported to the observer exactly once — the throw site sets `failureReported` so the catch-all doesn't double-report.

Tests must set `Retry.BaseDelay`/`MaxDelay` to zero or they sleep; `TestBase.CreateClient` already does.

The client never mutates the injected `HttpClient` (it may be pooled). Accept/User-Agent are set per request in `AddJsonHeaders`.

### Token acquisition

`BusinessCentralTokenProvider` owns the token cache: lock-free fast path, then a `SemaphoreSlim` with double-check before the client-credentials POST. Tokens are cached with a 60-second safety margin subtracted from `expires_in`. It uses `options.ResolvedTokenEndpoint` — never re-implement placeholder substitution locally.

**It is registered as a singleton on purpose.** Typed HTTP clients are transient, so a cache living on `BusinessCentralClient` would re-authenticate on every injection. It gets its own named `HttpClient` (`TokenHttpClientName`) via `IHttpClientFactory`. If you ever move token state back onto the client, you reintroduce that bug — `Token_Cache_Is_Shared_Across_Resolved_Clients` guards it.

### Options and placeholders

`BusinessCentralOptions` requires only four settings (TenantId, ClientId, ClientSecret, Company); `BaseUrl`, `TokenEndpoint`, `Environment` and `Scope` have working defaults. `{tenant}`, `{TenantId}` and `{environment}` are substituted by `ResolvedBaseUrl`/`ResolvedTokenEndpoint` — **always consume those, not the raw properties**. The validator checks the *resolved* URLs and rejects leftover `{...}`, which is what made the old README's `{tenant}` example fail silently.

### URL construction

All entity URLs go through `BusinessCentralUrlBuilder`, which injects the company segment: `{BaseUrl}/Company('{company}')/{path}`. `BuildServiceRootUrl` deliberately skips it — the company list is tenant-level. Three encoding rules, each deliberately different:

- `EncodePath` encodes **per segment**, so `/` keeps its meaning (navigation properties) while spaces are escaped.
- `EncodeKey` preserves OData key syntax (`'`, `=`, `,`, `(`, `)`) so alternate keys like `No='1000'` survive; `Uri.EscapeDataString` would mangle them into `No%3D%271000%27`.
- `BuildRawUrl` (used only by `QueryRawAsync`) passes everything after the first `?` through verbatim, so caller-supplied query strings like `salesOrders?$top=5` work.

Note the quirk in `BuildQueryUrl`: a filter string of `"true"` is treated as "no filter" and omitted.

### Filters and typed field names

`ODataFilter`'s constructor is `internal` — expressions can only be produced by `Filter` or `FilterExtensions`. New operators belong in those two files, and each needs **both** a string and an `Expression<Func<TEntity, object?>>` overload. `Filter.Format` handles type→OData literal conversion (invariant culture); extend it there when adding value types.

`PropertyPath.Resolve` turns a selector into a field name using `JsonPropertyNameAttribute` first, then `BusinessCentralJson.Options.PropertyNamingPolicy`. That coupling is the point: `$filter`/`$select`/`$orderby` names always match deserialization. It strips the compiler's boxing `Convert` and walks nested members into `a/b` navigation paths.

### Paging

Two paging implementations exist and must stay in agreement: `BusinessCentralClient.QueryStreamAsync` (path-based) and `BusinessCentralQuery<T>.StreamAsync` (fluent). Both use the same three-tier termination:

1. Follow `@odata.nextLink` whenever present, and set `serverDriven`.
2. Once `serverDriven`, a missing nextLink means the collection is exhausted — the `$top` short-page rule no longer applies.
3. Otherwise stop on the first page shorter than the requested size.

`QueryOptions.PageSize` is rows-per-round-trip; `Top` is a result cap. `PageSize ?? Top ?? 1000` keeps the old `WithTop`-as-page-size behaviour working.

### Writes

Each of POST/PATCH/PUT has two overloads, distinguished purely by **generic arity**:

- one-generic (`PostAsync<T>`) — payload and result share a type; `ReadEntityOrEchoAsync` returns the sent payload on `204`/empty body.
- two-generic (`PostAsync<TPayload, TResult>`) — `ReadEntityOrDefaultAsync` returns `default` on `204`/empty body, since the payload cannot stand in for the result. `TResult` is deliberately unconstrained so `JsonElement` works.

Arity is what keeps `PostAsync<dynamic>(path, payload)` binding to the one-generic form. `WriteOverloadTests` pins that; don't add an overload that changes arity resolution. Both forms send `Prefer: return=representation`, and `204` is a success in both. `DeleteAsync` accepts 200 or 204.

### Observability

There is no `ILogger` dependency. Instead the client takes an optional `IBusinessCentralObserver` and falls back to `NullBusinessCentralObserver` when none is registered. Consumers opt in with `services.AddObserver<MyObserver>()` (registered via `TryAddSingleton`). `OnTokenRefreshed` fires only on a real refresh; cache hits fire `OnTokenServedFromCache`, and retries fire `OnRequestRetrying`. Both are **default interface methods** so adding them didn't break existing implementers — use the same trick for future events. When adding one, update the interface, `NullBusinessCentralObserver`, `TestObserver`, and the observer tests.

### DI registration

Two `AddBusinessCentral` overloads (lambda and `IConfiguration`) share `AddBusinessCentralCore`, which wires options + `BusinessCentralOptionsValidator` (an `IValidateOptions<>` that names *each* missing setting rather than collapsing to one boolean), a named token `HttpClient`, the singleton `BusinessCentralTokenProvider`, and the typed client. Both HTTP clients are registered under explicit names (`TokenHttpClientName`, `ClientHttpClientName`) so tests can swap primary handlers without replacing the registration.

The typed client uses the explicit-factory overload of `AddHttpClient`, not `ActivatorUtilities` — the constructor taking `BusinessCentralTokenProvider` is `internal` and would otherwise never be selected.

## Tests

`test/Dynamics365.BusinessCentral.Tests/` — 121 xUnit facts, no mocking library. The csproj has `InternalsVisibleTo`, so internal types are directly testable. Suites: `ClientTests` (path-based API), `QueryBuilderTests` (fluent/typed), `RetryTests`, `OptionsTests`, `ObserverTests`, `ServiceCollectionExtensionsTests`.

Pattern: `TestBase.CreateClient(handler, observer?, configure?)` builds a real `BusinessCentralClient` over `FakeHttpHandler`, a `HttpMessageHandler` driven by a `Func<HttpRequestMessage, HttpResponseMessage>`. **Every handler must answer the token request first** — wrap it in `TestBase.WithToken(...)`, which does that for you, rather than repeating the `Contains("auth")` branch. `TestBase.Json(body)` is the 200-response shorthand. Test entity types live in `Utils/`; `SalesOrder` is the annotated one used for typed-query tests.

`ClientTests` is a `partial` class split by `#region` per operation; keep new tests grouped the same way.

## Docs

`README.md` and `src/Dynamics365.BusinessCentral/README.md` are identical except for one line: the root file links `MIGRATION.md` relatively, the packed copy uses an absolute GitHub URL because relative links don't resolve on nuget.org. Update both together when public API changes.

`MIGRATION.md` documents the 1.0 → 2.0 move. Add to it whenever a change alters behaviour silently — that file is the only place a silent change is discoverable.
