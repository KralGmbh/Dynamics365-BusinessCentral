# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`Dynamics365.BusinessCentral` — a NuGet library: a lightweight, strongly-typed client for the Dynamics 365 Business Central OData v4 API. Three runtime dependencies (`Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options.ConfigurationExtensions`); everything else is `HttpClient` + `System.Text.Json`. Staying near-dependency-free is a deliberate design goal — don't add packages casually.

A companion package, `src/Dynamics365.BusinessCentral.Testing/` (`FakeBusinessCentral`), runs a real client over a scripted transport so consumers can assert the exact OData their code produces. It is versioned in lockstep with the main csproj and ships from the same release. Two rules: it depends only on the main package's **public** API (no `InternalsVisibleTo` — if the fake needs something internal, that something probably wants to be public), and it deliberately contains **no OData query evaluation** — an in-memory `$filter` engine would be a permanent compatibility liability. Keep it a transport fake. Its contract is pinned by `FakeBusinessCentralTests` in the main test project.

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

dotnet pack -c Release          # packs BOTH packages (main + Testing), .nupkg + .snupkg each
```

CI: `.github/workflows/sonar.yml` builds + tests **all three TFMs** and runs SonarCloud on every push/PR to `master`. Both workflows install the 8.0.x/9.0.x/10.0.x runtimes via `setup-dotnet` — the 10.0.x SDK can *build* net8.0/net9.0 from NuGet targeting packs, but their test hosts need the matching runtimes, and depending on the runner image to preinstall them is what previously left net9.0 executed nowhere. Don't reintroduce `-f net8.0` on the gate; it belongs only on the Sonar coverage run, where a second and third pass over the same sources would not move the number. `.github/workflows/nuget.yml` **tests before it packs** and pushes on published GitHub releases — a release is cut from a tag, which need not be the commit CI last saw. Bump `<Version>` in both csprojs before tagging.

## Architecture

`src/Dynamics365.BusinessCentral/` — one assembly, folders by concern:

- **`Client/`** — `BusinessCentralClient` (sealed, the only implementation of `IBusinessCentralClient`) plus the internal `BusinessCentralTokenProvider`, `BusinessCentralUrlBuilder` and `HttpRequestExtensions`. Every `IBusinessCentralClient` member has a **default implementation** so hand-written consumer fakes keep compiling across interface growth — a new member must either throw via the interface's private `NotImplemented` helper or, when it can be composed from other members (like `FirstOrDefaultAsync` over `QueryAsync`), do that so fakes inherit it. `DefaultInterfaceTests` pins the contract.
- **`OData/`** — the typed query surface: `IBusinessCentralQuery<T>`/`BusinessCentralQuery<T>` (fluent builder), `PropertyPath` (selector → field name), `EntityPath` + `BusinessCentralEntityAttribute` (type → entity set), `Filter`/`ODataFilter`/`FilterExtensions`, `QueryOptions`, `ODataResponse<T>`, `BusinessCentralPage<T>`, `BusinessCentralCompany`.
- **`Options/`** — `BusinessCentralOptions` (credentials, base URL, company), `BusinessCentralOptionsValidator`, and `BusinessCentralJson.Options`, the single shared `JsonSerializerOptions` (camelCase policy, case-insensitive read) used by both the client and the exception factory.
- **`Errors/`** — `BusinessCentralException` base + six sealed subtypes (incl. `BusinessCentralThrottledException` and `BusinessCentralConnectionException`, whose `StatusCode` is `0` — no response); `BusinessCentralExceptionFactory` maps status codes and parses `Retry-After`. The subtypes are sealed siblings, so the base carries predicates (`IsNotFound`, `IsThrottled`, `IsValidation`, `IsAuth`, `IsConnectionFailure`) — add one when adding a subtype.
- **`Diagnostics/`** — `IBusinessCentralObserver` and its info DTOs.
- **`ServiceCollectionExtensions.cs`** — `AddBusinessCentral` / `AddObserver<T>`.

### Request pipeline

Everything funnels through `BusinessCentralClient.SendWithAuthRetryAsync`, a single loop with **two independent retry budgets**:

- **auth** — one retry, tracked by `authRetried`. A `401` invalidates the token and retries once.
- **transient** — `BusinessCentralRetryOptions.MaxAttempts`, tracked by `transientAttempt`. Gated by `IsSafeToReplay`, not by `IsTransient` alone. `RetryHelper.ComputeDelay` (shared with the token provider) prefers the server's `Retry-After`, else doubles `BaseDelay`; both are jittered additively by `JitterFactor` (never below a `Retry-After` — it's a minimum wait) and capped by `MaxDelay`.

`IsSafeToReplay` encodes a deliberate asymmetry: a `429` is rejected *before* processing so it is always replayable, but `408/502/503/504` are ambiguous — the write may already have landed. A `POST` is therefore not replayed on those unless `Retry.RetryPostOnTransientFailures` is set, because a duplicate row is worse than a surfaced error. `GET`/`PUT`/`DELETE` are idempotent and always retried. Don't "simplify" this back to a plain `IsTransient` check.

Each attempt builds a fresh request via the `createRequest` factory passed to `SendWithAuthRetryAsync` (a sent `HttpRequestMessage` can't be reused). A failure is reported to the observer exactly once — the throw site sets `failureReported` so the catch-all doesn't double-report — and a caller-requested cancellation is not reported at all. Observers are wrapped in `SafeBusinessCentralObserver` (`Diagnostics/`), which swallows callback exceptions: diagnostics must never break the pipeline, so never call a consumer observer directly.

Responses are **deliberately buffered as strings** before deserialization: the raw body on `BusinessCentralException.ResponseBody` and in `OnDeserializationFailed` is the package's most valuable field diagnostic (a consumer debugged a prod deserialization failure from a single log line). Under server-driven paging the default page can be 20k rows, so this costs memory — `BusinessCentralOptions.MaxPageSize` is the documented knob. Don't switch to stream deserialization without solving the lost-body diagnostics.

Tests must set `Retry.BaseDelay`/`MaxDelay` to zero or they sleep; `TestBase.CreateClient` already does.

The client never mutates the injected `HttpClient` (it may be pooled). Accept/User-Agent are set per request in `AddJsonHeaders`.

### Token acquisition

`BusinessCentralTokenProvider` owns the token cache: lock-free fast path, then a `SemaphoreSlim` with double-check before the client-credentials POST. Tokens are cached with a 60-second safety margin subtracted from `expires_in`. It uses `options.ResolvedTokenEndpoint` — never re-implement placeholder substitution locally.

The token POST retries transient failures under the same `Retry` options as data requests, **inside the lock** — every waiter needs the same token, so backoff for one is backoff for all. Replay is unconditionally safe (`client_credentials` has no side effects), so it gates on `IsTransient` alone, not `IsSafeToReplay`; credential failures (`400`/`401`) throw immediately. `InvalidateAsync(staleToken, …)` is compare-and-swap: it only clears the cache when the rejected token is still the cached one, so concurrent `401`s cannot cascade refreshes.

**It is registered as a singleton on purpose.** Typed HTTP clients are transient, so a cache living on `BusinessCentralClient` would re-authenticate on every injection. It gets its own named `HttpClient` (`TokenHttpClientName`) via `IHttpClientFactory`. If you ever move token state back onto the client, you reintroduce that bug — `Token_Cache_Is_Shared_Across_Resolved_Clients` guards it.

### Options and placeholders

`BusinessCentralOptions` requires only four settings (TenantId, ClientId, ClientSecret, Company); `BaseUrl`, `TokenEndpoint`, `Environment` and `Scope` have working defaults. `{tenant}`, `{TenantId}` and `{environment}` are substituted by `ResolvedBaseUrl`/`ResolvedTokenEndpoint` — **always consume those, not the raw properties**. The validator checks the *resolved* URLs and rejects leftover `{...}`, which is what made the old README's `{tenant}` example fail silently.

### URL construction

All entity URLs go through `BusinessCentralUrlBuilder`, which injects the company segment: `{BaseUrl}/Company('{company}')/{path}`. `BuildServiceRootUrl` deliberately skips it — the company list is tenant-level. Three encoding rules, each deliberately different:

- `EncodePath` encodes **per segment**, so `/` keeps its meaning (navigation properties) while spaces are escaped.
- `EncodeKey` preserves OData key syntax (`'`, `=`, `,`, `(`, `)`) so alternate keys like `No='1000'` survive; `Uri.EscapeDataString` would mangle them into `No%3D%271000%27`.
- `BuildRawUrl` (used only by `QueryRawAsync`) passes everything after the first `?` through verbatim, so caller-supplied query strings like `salesOrders?$top=5` work.

Note the quirk in `BuildQueryUrl`: a filter string of `"true"` is treated as "no filter" and omitted.

Every URL the builder assembles passes through `Guard`, which reports past `UrlLengthWarningThreshold` (observer `OnUrlLengthWarning`) and throws past `MaxUrlLength`. The two-level design is the point: a bare limit would turn queries BC currently accepts into client-side exceptions on upgrade, so the band between them is a measurement window. The builder is also the right seam because a server-issued `@odata.nextLink` never passes through it — `FetchNextPageAsync` sends the absolute URL verbatim — so continuations are exempt for free. Public entry points call `Guard` exactly once each; the internal `EntityUrl` helpers exist so composed builders don't double-report.

### Filters and typed field names

`ODataFilter`'s constructor is `internal` — expressions can only be produced by `Filter`, `FilterExtensions` or `IFilterBuilder<T>` (the type-inferred form used inside `Query<T>().Where(f => ...)`; `FilterBuilder<T>` is a stateless per-closed-generic singleton that forwards to `Filter`'s typed overloads). New operators belong in `Filter`/`FilterExtensions` and need **three** surfaces kept in sync: a string overload, an `Expression<Func<TEntity, object?>>` overload, and the `IFilterBuilder<T>` member — the parity test in `FluentSelectAndFilterBuilderTests` fails if the builder lags. `Filter.Format` handles type→OData literal conversion (invariant culture); extend it there when adding value types.

`PropertyPath.Resolve` turns a selector into a field name using `JsonPropertyNameAttribute` first, then `BusinessCentralJson.Options.PropertyNamingPolicy`. `EntitySelect` derives the fluent builder's default `$select` (settable scalar props, no `[JsonIgnore]`, no `@`-names, ordinal-sorted) through the same `PropertyPath.ResolveName` — never re-implement the name rules. Explicit `Select(...)` wins; `SelectAll()` suppresses; `CountAsync` sends none. That coupling is the point: `$filter`/`$select`/`$orderby` names always match deserialization. It strips the compiler's boxing `Convert` and walks nested members into `a/b` navigation paths.

`EntitySelect` cannot validate against the tenant, so a derived name that isn't a real column fails the query with a `400`. There is exactly **one** such cause — a property mapping to no BC column, which is breakage the derivation *creates* (it used to bind as its default). `BusinessCentralQuery.ExecutePageAsync` routes every fetch through `DerivedSelectHint`, which re-wraps a `400` with the derivation, the implicated property and both remedies.

**Never reintroduce a casing explanation anywhere** — hint, XML docs, README, MIGRATION. Alpha.6 documented `$select` as case-sensitive server-side; live-tenant measurement (`METADATA-PROBE-FINDINGS-BASTION.md`) showed the opposite, and BC answers in its own canonical casing regardless of what was requested. Since casing drift cannot produce this `400`, naming it misdirects 100% of real occurrences away from the answer the server already gave. `Hint_Makes_No_Case_Sensitivity_Claim` is the regression guard; the wording has drifted twice. Re-wrapping needs `BusinessCentralException.ServerMessage` (the undecorated message) or the `(GET → HTTP 400)` suffix accumulates. `UsesDerivedSelect` gates it, so explicit `Select`, `SelectAll`, `CountAsync` and the path-based API never see the hint.

### Paging

The auto-paging state machine lives **once**, in `QueryPager` (internal, `OData/`); both public entry points — `BusinessCentralClient.QueryStreamAsync` (path-based) and `BusinessCentralQuery<T>.StreamAsync` (fluent) — delegate to it and differ only in their fetch delegates. Paging is **server-driven** (measured against a live BC SaaS tenant — see `NEXTLINK-FINDINGS-BASTION.md`): the first request carries `$top` only when the caller set a result cap and `$skip` only when they set an offset; the resolved page preference (`QueryOptions.PageSize ?? BusinessCentralOptions.MaxPageSize ?? nothing`) is sent as `Prefer: odata.maxpagesize` on the first request **and every continuation** (the preference is per request, not per cursor); the server pages at its own Max Page Size otherwise. Termination: no `@odata.nextLink` means done — either the server served everything or the `$top` budget is satisfied. There is no `$skip` loop and no package page-size constant; don't reintroduce either. Single-page reads (`QueryAsync`, `ToListAsync`, `GetAsync`, …) pass a null preference on purpose — `odata.maxpagesize` on a one-shot request silently truncates it.

`QueryOptions.PageSize` is the server-page preference; `Top` is a result cap (sent as `$top`, enforced client-side mid-page). The old `WithTop`-as-page-size behaviour is gone (2.0).

### Writes

Each of POST/PATCH/PUT has two overloads, distinguished purely by **generic arity**:

- one-generic (`PostAsync<T>`) — payload and result share a type; `ReadEntityOrEchoAsync` returns the sent payload on `204`/empty body.
- two-generic (`PostAsync<TPayload, TResult>`) — `ReadEntityOrDefaultAsync` returns `default` on `204`/empty body, since the payload cannot stand in for the result. `TResult` is deliberately unconstrained so `JsonElement` works.

Arity is what keeps `PostAsync<dynamic>(path, payload)` binding to the one-generic form. `WriteOverloadTests` pins that; don't add an overload that changes arity resolution. Both forms send `Prefer: return=representation`, and `204` is a success in both. `DeleteAsync` accepts 200 or 204.

### Observability

There is no `ILogger` dependency. Instead the client takes an optional `IBusinessCentralObserver` and falls back to `NullBusinessCentralObserver` when none is registered. Consumers opt in with `services.AddObserver<MyObserver>()` (registered via `TryAddSingleton`). `OnTokenRefreshed` fires only on a real refresh; cache hits fire `OnTokenServedFromCache`, retries fire `OnRequestRetrying`, and over-long URLs fire `OnUrlLengthWarning`. All three are **default interface methods** so adding them didn't break existing implementers — use the same trick for future events. When adding one, update the interface, `NullBusinessCentralObserver`, `SafeBusinessCentralObserver`, `TestObserver`, and the observer tests.

### DI registration

Two `AddBusinessCentral` overloads (lambda and `IConfiguration`) share `AddBusinessCentralCore`, which wires options + `BusinessCentralOptionsValidator` (an `IValidateOptions<>` that names *each* missing setting rather than collapsing to one boolean), a named token `HttpClient`, the singleton `BusinessCentralTokenProvider`, and the typed client. Both HTTP clients are registered under explicit names — public via `BusinessCentralHttpClients.Token`/`.Client`, a deliberate API so consumers can exempt them from global resilience handlers — so tests can swap primary handlers without replacing the registration.

The typed client uses the explicit-factory overload of `AddHttpClient`, not `ActivatorUtilities` — the constructor taking `BusinessCentralTokenProvider` is `internal` and would otherwise never be selected.

## Tests

`test/Dynamics365.BusinessCentral.Tests/` — ~300 xUnit facts, no mocking library. The csproj has `InternalsVisibleTo`, so internal types are directly testable. Suites: `ClientTests` (path-based API, `partial`, split by `#region`), `QueryBuilderTests` (fluent/typed), `RetryTests` (incl. token acquisition and network failures), `RetryDelayTests` (jitter contract), `TokenProviderTests` (CAS invalidation), `FilterFormatTests` (OData literals), `DefaultInterfaceTests` (the fakes-keep-compiling contract), `ExceptionPredicateTests`, `BusinessCentralFieldTests`, `FakeBusinessCentralTests` (the Testing package's contract), plus `OptionsTests`, `OptionsValidatorTests`, `ObserverTests`, `PagingGuardTests`, `RequestReplayTests`, `WriteOverloadTests`, `ServiceCollectionExtensionsTests`, `FluentSelectAndFilterBuilderTests` (F1/F2), `UrlLengthGuardTests`, `DerivedSelectDiagnosticsTests`.

Pattern: `TestBase.CreateClient(handler, observer?, configure?)` builds a real `BusinessCentralClient` over `FakeHttpHandler`, a `HttpMessageHandler` driven by a `Func<HttpRequestMessage, HttpResponseMessage>`. **Every handler must answer the token request first** — wrap it in `TestBase.WithToken(...)`, which does that for you, rather than repeating the `Contains("auth")` branch. `TestBase.Json(body)` is the 200-response shorthand. Test entity types live in `Utils/`; `SalesOrder` is the annotated one used for typed-query tests.

`ClientTests` is a `partial` class split by `#region` per operation; keep new tests grouped the same way.

## Docs

`README.md` and `src/Dynamics365.BusinessCentral/README.md` are identical except for one line: the root file links `MIGRATION.md` relatively, the packed copy uses an absolute GitHub URL because relative links don't resolve on nuget.org. Update both together when public API changes. A third README, `src/Dynamics365.BusinessCentral.Testing/README.md`, ships in the Testing package — update it when the fake's API changes.

Releasing bumps `<Version>` in **both** csprojs — the packages are versioned in lockstep, and the Testing package's dependency on the main package comes from its `ProjectReference`, so a mismatched bump publishes a dangling dependency.

Three release docs, each with a distinct job — don't merge them:

- `CHANGELOG.md` — **what** changed, every version, Keep a Changelog format. Add an entry under `## [Unreleased]` as part of the change itself, not at release time.
- `MIGRATION.md` — **how** to upgrade across a breaking version. Add to it whenever a change alters behaviour *silently*; that file is the only place such a change is discoverable.
- `<PackageReleaseNotes>` in the csproj — the nuget.org summary. Keep it to a few lines that link the other two; nuget.org renders it as plain text, so never duplicate content there.

Releasing: bump `<Version>`, move `[Unreleased]` to the new version with a date, refresh `<PackageReleaseNotes>`, then tag. `.github/workflows/nuget.yml` packs and pushes on published GitHub releases.
