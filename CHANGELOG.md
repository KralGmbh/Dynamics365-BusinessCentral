# Changelog

All notable changes to **Dynamics365.BusinessCentral** are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.0-alpha] - 2026-07-28

**Prerelease.** Published for validation against real Business Central environments before
2.0.0 stable. Everything below is complete and tested, but every test runs against a fake
HTTP handler — see *Unverified* at the end of this entry for what still needs confirming
against a live tenant.

Install with `dotnet add package Dynamics365.BusinessCentral --prerelease`, or pin the
version explicitly.

Six defects and a rework of the consumer-facing API. **Upgrading from 1.x? Start with
[MIGRATION.md](MIGRATION.md)** — most code keeps compiling, but several behavioural changes
are silent.

### Fixed

- **Access tokens are no longer re-fetched on every injection.** The cache lived on the
  client, but typed `HttpClient`s are registered transient, so every resolution of
  `IBusinessCentralClient` triggered a fresh token request. It now lives on a singleton.
- **`QueryRawAsync` accepts a query string.** It previously percent-encoded the whole path,
  turning `salesOrders?$top=5` into `salesOrders%3F%24top%3D5` — the documented usage never
  worked. Path separators are also preserved now, so navigation paths work.
- **Entity keys keep their OData syntax.** `Uri.EscapeDataString` was applied to the whole
  key, mangling alternate keys such as `No='1000'` into `No%3D%271000%27`.
- **`BaseUrl` placeholders are substituted.** `{tenant}` was documented but never replaced,
  so the README's own example put a literal placeholder into every request URL.
- **`204 No Content` on a write is a success.** `POST`/`PATCH`/`PUT` previously raised
  `BusinessCentralServerException` for a write the server had applied.
- **`@odata.nextLink` is followed.** `QueryAllAsync` silently truncated whenever Business
  Central drove paging with a page smaller than the requested `$top`.
- **`OnRequestFailed` is raised once per failure**, not twice.
- **Chained ordering no longer discards keys.** `OrderByAsc(a).OrderByAsc(b)` dropped `a`;
  use the new `ThenByAsc`/`ThenByDesc` to append.
- **`Filter.In` with an empty collection** yields `false` instead of the invalid OData
  expression `field in ()`.
- **Non-positive paging values no longer hang.** A page size of zero made the "short page"
  termination check unreachable, so streaming and `QueryAllAsync` requested the same empty
  page forever. `PageSize` now requires a positive value, `Top`/`Skip` reject negatives, and
  a zero-row request returns immediately.
- **Short-lived tokens are cached.** Subtracting the fixed 60-second safety margin from an
  `expires_in` below 60 put the expiry in the past, so the token counted as expired on
  arrival and every call re-authenticated.
- **The `User-Agent` reports the real version**, derived from the assembly rather than a
  hard-coded `1.0`.
- **Retrying a write no longer throws `ObjectDisposedException`.** Retries reused the
  original request, but `HttpClient` disposes request content once a send completes, so any
  replayed `POST`/`PATCH`/`PUT` — including the automatic retry after a `401` — failed on
  the second attempt. Each attempt now builds a fresh request.
- **A caller-supplied `$skip` is honoured** by `QueryAllAsync`/`QueryStreamAsync`, which
  previously always restarted from the first page.
- **Abandoned responses are disposed** on the retry paths, and responses consumed internally
  are scoped with `using`.
- **`QueryRawAsync<JsonElement>` compiles.** The README has shown that call since 1.0, but
  `TResponse` was constrained to reference types, so the documented example never built for
  consumers. The constraint is removed, matching the unconstrained result type on the
  two-generic write overloads.
- **Failed responses are released before the backoff sleep** rather than after, so a
  throttling window no longer holds buffered responses open across concurrent callers.
- **Retry backoff cannot overflow.** A large `BaseDelay` combined with a high `MaxAttempts`
  produced a value `TimeSpan` cannot represent, and `TimeSpan.FromMilliseconds` throws on
  that — turning a transient failure into an unrelated crash. Delays are now clamped, and
  negative configured values floor at zero.
- `BusinessCentralErrorInfo.ResponseBody` was declared but never populated.
- The constructor no longer mutates the injected `HttpClient`, which may be pooled.

### Added

- **Typed query builder.** `client.Query<T>()` with `Where`, `OrderBy`/`ThenBy`, `Select`,
  `Expand`, `Top`, `Skip`, `PageSize`, and terminal `ToListAsync`, `ToAllAsync`,
  `StreamAsync`, `ToPageAsync`, `FirstOrDefaultAsync`, `CountAsync`. Field names come from
  property selectors resolved through the same `JsonSerializerOptions` used for
  deserialization, so filters and projections cannot drift from the model.
- **`[BusinessCentralEntity]`** binds a type to its OData entity set, so the path is not
  repeated at every call site.
- **Typed `Filter` overloads** — `Filter.Equals<SalesOrder>(o => o.Status, "Open")` — for
  every existing operator.
- **Automatic retry** of throttled and transient failures, honouring `Retry-After`.
  Configurable through `BusinessCentralOptions.Retry`.
- **`BusinessCentralThrottledException`** for `429`, plus `IsTransient` and `RetryAfter` on
  every exception.
- **`$expand` and `$count`**, via the builder or `QueryOptions`.
- **Streaming** — `QueryStreamAsync` and `IBusinessCentralQuery.StreamAsync` return
  `IAsyncEnumerable<T>` and stop fetching when enumeration stops.
- **Multi-company support** — `ForCompany(name)` shares the HTTP client and token cache;
  `GetCompaniesAsync()` lists the tenant's companies.
- **`AddBusinessCentral(IConfiguration)`** for `appsettings.json` binding.
- **Payload/result write overloads** — `PostAsync<TPayload, TResult>` and the same for
  `PATCH`/`PUT`, so posting an anonymous object and reading back a typed entity no longer
  requires `dynamic`.
- **`QueryOptions.WithPageSize`**, distinct from `WithTop`.
- **`OnTokenServedFromCache` and `OnRequestRetrying`** observer events, both default
  interface methods so existing observers keep compiling.
- **XML documentation ships in the package** — consumers previously got no IntelliSense.
- SourceLink metadata, so the published `.snupkg` supports step-into debugging.

### Changed

- **Only four settings are required** — `TenantId`, `ClientId`, `ClientSecret`, `Company`.
  `BaseUrl`, `TokenEndpoint`, `Environment` and `Scope` have working defaults, and
  `{tenant}`/`{environment}` are substituted.
- **`Exception.Message` is a single line.** It previously embedded the status, URL and the
  entire response body, flooding structured logs. Those are properties, and `ToString()`
  renders everything.
- **Writes send `Prefer: return=representation`**, so Business Central returns the affected
  entity where it can.
- **`POST` is not replayed on ambiguous transient failures.** A `429` is rejected before
  processing so replay is safe, but `408`/`502`/`503`/`504` may mean the write landed —
  replaying would duplicate it. `GET`/`PUT`/`DELETE` are retried normally, and `PATCH` is
  too, because this client only sends absolute field values so a replay converges. Opt
  `POST` back in with `Retry.RetryPostOnTransientFailures`.
- **Options validation names each missing setting** instead of collapsing everything into
  one boolean, and rejects unsubstituted `{…}` placeholders.
- `ConfigureAwait(false)` throughout, so sync-over-async callers cannot deadlock.
- Package dependencies are declared per target framework rather than pinning every target
  to the 8.0.0 floor.
- `ExcludeFromCodeCoverage` removed from `ServiceCollectionExtensions`, now that the DI
  wiring carries real logic and is covered by tests.

### Notes

- `429` is now `BusinessCentralThrottledException`, a **sibling** of
  `BusinessCentralServerException` rather than a subclass. `catch (BusinessCentralServerException)`
  no longer sees it. (The same was already true of `400`/`401`/`403`/`404` — see
  [MIGRATION.md](MIGRATION.md).)
- Test suite grew from 80 to 159, green on net8.0/net9.0/net10.0 with zero warnings.
- No Native AOT or trimming support yet; `System.Text.Json` reflection and the reflection
  used for typed selectors both block it.

### Unverified

Known gaps in this prerelease, to be confirmed before 2.0.0 stable:

- `GetCompaniesAsync` targets `{BaseUrl}/Company`. `BusinessCentralCompany.Name` is
  reliable; `DisplayName`, `Id` and `IsEvaluationCompany` are best-effort and null when the
  endpoint does not return them. Not checked against a live tenant.
- Whether Business Central honours `Prefer: return=representation` on a given endpoint,
  which determines how often the `204 No Content` path is taken on writes.
- Paging against a real dataset large enough to trigger server-driven `@odata.nextLink`.

## [1.0.0] - 2026-01-20

First stable release.

### Added

- `POST`, `PUT` and `DELETE` support alongside querying and `PATCH`.
- Diagnostics observer (`IBusinessCentralObserver`) for request and token lifecycle events.
- OData-aware exception factory mapping status codes to typed exceptions and extracting the
  Business Central correlation ID.

### Changed

- Simplified dependency-injection registration.
- Improved token caching and concurrency handling.
- Reworked URL building.

## [0.1.5] - [0.1.10] - 2026-01-16 - 2026-01-19

Initial pre-release line: OData querying with fluent filters, client-credentials
authentication, DI integration, multi-targeting and NuGet packaging.

[Unreleased]: https://github.com/KralGmbh/Dynamics365-BusinessCentral/compare/v2.0.0-alpha...HEAD
[2.0.0-alpha]: https://github.com/KralGmbh/Dynamics365-BusinessCentral/compare/v1.0.0...v2.0.0-alpha
[1.0.0]: https://github.com/KralGmbh/Dynamics365-BusinessCentral/compare/v0.1.10...v1.0.0
