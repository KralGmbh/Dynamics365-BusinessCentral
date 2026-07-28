# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`Dynamics365.BusinessCentral` — a NuGet library: a lightweight, strongly-typed client for the Dynamics 365 Business Central OData v4 API. Only two runtime dependencies (`Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Http`); everything else is `HttpClient` + `System.Text.Json`. Keeping it dependency-free is a deliberate design goal — don't add packages casually.

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
- **`OData/`** — `Filter` (factory) → `ODataFilter` (immutable expression) → `FilterExtensions` (`And`/`Or`/`Not`), and `QueryOptions` (`$top`/`$skip`/`$orderby`).
- **`Options/`** — `BusinessCentralOptions` (credentials, base URL, company), `BusinessCentralOptionsValidator`, and `BusinessCentralJson.Options`, the single shared `JsonSerializerOptions` (camelCase policy, case-insensitive read) used by both the client and the exception factory.
- **`Errors/`** — `BusinessCentralException` base + four sealed subtypes; `BusinessCentralExceptionFactory` maps status codes to them.
- **`Diagnostics/`** — `IBusinessCentralObserver` and its info DTOs.
- **`ServiceCollectionExtensions.cs`** — `AddBusinessCentral` / `AddObserver<T>`.

### Request pipeline

Everything funnels through `BusinessCentralClient.SendWithAuthRetryAsync`. It runs a 2-attempt loop: acquire token → clone the request (`HttpRequestExtensions.Clone`, because a sent `HttpRequestMessage` can't be reused) → send. A `401` on attempt 0 invalidates the cached token and retries once; any other non-success status throws via `BusinessCentralExceptionFactory.CreateAsync`. A failure is reported to the observer exactly once — the throw site sets `failureReported` so the catch-all doesn't double-report.

The client never mutates the injected `HttpClient` (it may be pooled). Accept/User-Agent are set per request in `AddJsonHeaders`.

### Token acquisition

`BusinessCentralTokenProvider` owns the token cache: lock-free fast path, then a `SemaphoreSlim` with double-check before the client-credentials POST. Tokens are cached with a 60-second safety margin subtracted from `expires_in`. `{TenantId}` in `TokenEndpoint` is substituted at request time.

**It is registered as a singleton on purpose.** Typed HTTP clients are transient, so a cache living on `BusinessCentralClient` would re-authenticate on every injection. It gets its own named `HttpClient` (`TokenHttpClientName`) via `IHttpClientFactory`. If you ever move token state back onto the client, you reintroduce that bug — `Token_Cache_Is_Shared_Across_Resolved_Clients` guards it.

### URL construction

All entity URLs go through `BusinessCentralUrlBuilder`, which always injects the company segment: `{BaseUrl}/Company('{company}')/{path}`. Three encoding rules, each deliberately different:

- `EncodePath` encodes **per segment**, so `/` keeps its meaning (navigation properties) while spaces are escaped.
- `EncodeKey` preserves OData key syntax (`'`, `=`, `,`, `(`, `)`) so alternate keys like `No='1000'` survive; `Uri.EscapeDataString` would mangle them into `No%3D%271000%27`.
- `BuildRawUrl` (used only by `QueryRawAsync`) passes everything after the first `?` through verbatim, so caller-supplied query strings like `salesOrders?$top=5` work.

Note the quirk in `BuildQueryUrl`: a filter string of `"true"` is treated as "no filter" and omitted.

### Filters

`ODataFilter`'s constructor is `internal` — expressions can only be produced by `Filter` or `FilterExtensions`. New operators belong in those two files, not at call sites. `Filter.Format` handles the type→OData literal conversion (invariant culture); extend it there when adding value types.

### Paging

`QueryAllAsync` interprets `QueryOptions.Top` as *page size* (default 1000) rather than a total cap. Termination is two-tier: follow `@odata.nextLink` whenever the server sends one (server-driven paging means a short page is **not** the end), otherwise stop on the first page shorter than the requested size. It preserves `OrderBy` across pages but overwrites `Top`/`Skip`.

### Writes

POST/PATCH/PUT send `Prefer: return=representation` and go through `ReadEntityOrEchoAsync`: a `204 NoContent` or empty body is a **success** that returns the payload that was sent, not an error. `DeleteAsync` accepts 200 or 204.

### Observability

There is no `ILogger` dependency. Instead the client takes an optional `IBusinessCentralObserver` and falls back to `NullBusinessCentralObserver` when none is registered. Consumers opt in with `services.AddObserver<MyObserver>()` (registered via `TryAddSingleton`). `OnTokenRefreshed` fires only on a real refresh; cache hits fire `OnTokenServedFromCache`, which is a **default interface method** so adding it didn't break existing implementers — use the same trick for future events. When adding one, update the interface, `NullBusinessCentralObserver`, `TestObserver`, and `ObserverTests`.

### DI registration

`AddBusinessCentral` wires four things: options + `BusinessCentralOptionsValidator` (an `IValidateOptions<>` that names *each* missing setting rather than collapsing to one boolean), a named token `HttpClient`, the singleton `BusinessCentralTokenProvider`, and the typed client. Both HTTP clients are registered under explicit names (`TokenHttpClientName`, `ClientHttpClientName`) so tests can swap primary handlers without replacing the registration.

The typed client uses the explicit-factory overload of `AddHttpClient`, not `ActivatorUtilities` — the constructor taking `BusinessCentralTokenProvider` is `internal` and would otherwise never be selected.

## Tests

`test/Dynamics365.BusinessCentral.Tests/` — 80 xUnit facts, no mocking library. The csproj has `InternalsVisibleTo`, so internal types are directly testable.

Pattern: `TestBase.CreateClient(handler, observer?)` builds a real `BusinessCentralClient` over `FakeHttpHandler`, a `HttpMessageHandler` driven by a `Func<HttpRequestMessage, HttpResponseMessage>`. **Every handler must answer the token request first** — check `req.RequestUri.AbsoluteUri.Contains("auth")` and return `{"access_token":"abc","expires_in":3600}`, otherwise the request never reaches the assertion. Test entity types live in `Utils/`.

`ClientTests` is a `partial` class split by `#region` per operation; keep new tests grouped the same way.

## Docs

`README.md` and `src/Dynamics365.BusinessCentral/README.md` are byte-identical — the latter is the packed NuGet readme. Update both together when public API changes.
