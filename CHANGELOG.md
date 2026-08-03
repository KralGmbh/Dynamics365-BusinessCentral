# Changelog

All notable changes to **Dynamics365.BusinessCentral** are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.0-alpha.7] - 2026-08-03

**Prerelease.** The URL-length guard (from the pre-stable review, sharpened by the
live-tenant round), plus the correction of
an alpha.6 claim about derived `$select` that was wrong in one specific case.

### Added

- **`BusinessCentralOptions.MaxQueryStringLength`** (default `8000`) and
  **`QueryStringLengthWarningThreshold`** (default `6000`). A request whose query string
  passes the threshold raises the new `IBusinessCentralObserver.OnUrlLengthWarning` and is
  **still sent**; one past the limit throws an `ArgumentException` before the request leaves
  the process, naming the length, the limit and — when the filter is an `or`-chain — the
  clause count and `Filter.In` as the likely cause. Either setting may be `null` to disable it.

  **The query string, not the whole URL**, because that is what Business Central's gateway
  limits — measured at **8,099** accepted characters, invariant across two environments whose
  full URLs differed by the length of their prefixes. The prefix moves with environment name,
  company name (`Company('KRAL%20AG')`, where the escaped space inflates it) and entity-set
  path, so a full-URL limit is simultaneously too strict on deployments with long prefixes
  and too loose on short ones. Both defaults are set from that measurement rather than
  reasoned from IIS limits, with headroom under the ceiling rather than sitting on it.

  The gap between the two is deliberate. A hard threshold alone would turn queries Business
  Central currently accepts into client-side exceptions on upgrade; the warning band instead
  lets a deployment measure the length distribution its real workload produces and size
  chunking against evidence.

  Past its own ceiling the server answers `414 URI Too Long` — not the opaque `400`/`404`
  earlier drafts of this entry claimed. The guard's value is therefore pre-flight diagnosis
  (which filter, how many `or` clauses, and that `Filter.In` is the likely cause), not
  decoding an unhelpful status.

  Server-issued `@odata.nextLink` continuations are never checked: the server produced them,
  so its own limits already applied.

- **`IBusinessCentralObserver.OnUrlLengthWarning`** with `BusinessCentralUrlLengthInfo`
  (`Url`, `UrlLength`, `QueryStringLength`, `Threshold`, `Limit`, `ExceedsLimit`,
  `OrClauseCount`). A default interface method, so existing observers keep compiling.
  `QueryStringLength` is the measured quantity; `UrlLength` is kept alongside it because the
  difference between them is exactly what makes a full-URL limit unportable.

- **`BusinessCentralMetadata` in the Testing package (M4): the derived-`$select` hazard is now
  checkable in CI**, not just documented.

  ```csharp
  await BusinessCentralMetadata.AssertProjectionsResolveAsync(client, typeof(Item).Assembly);
  ```

  Fetches the tenant's `$metadata`, derives the `$select` for every `[BusinessCentralEntity]`
  type, and throws listing **every** name that matches no column — not the first, because the
  alternative is learning the same thing one production `400` at a time. `ValidateAsync`
  returns the report instead of throwing; `Parse` and `Validate` are pure and take a canned
  document, so the half with the logic in it is unit-testable without a tenant.

  This ships **with** F2 rather than after it, because separating them is what would have made
  default-on risky: nothing between "upgrade" and "production" otherwise detects a property
  that is not a column. A consumer's unit suite cannot — mocks do not validate `$select`, and
  neither does `FakeBusinessCentral`, which proves what OData you generate and never what the
  tenant accepts. Column matching is case-**insensitive**, following the measurement above; an
  ordinal match here would report working projections as broken.

- **`IBusinessCentralClient.GetMetadataAsync`** returns the raw `$metadata` EDMX as a string.
  A default interface method. Deliberately not parsed into a model: the package does not own an
  EDMX object model and inventing one would be a lasting compatibility liability. The document
  is large (~8 MB on a real tenant), so it is a build- or startup-time call.

- **`EntitySelect` is public**, with a non-generic `For(Type)` for callers that discover entity
  types by reflection. It was internal; the Testing package depends only on the main package's
  public API, and the validator needs exactly the projection the fluent builder would send —
  reimplementing those rules is precisely the drift this feature exists to prevent.

- **`BusinessCentralOptions.DataAccessIntent`** (default `Unspecified`): sends
  `Data-Access-Intent: ReadOnly`, letting Business Central answer reads from a database
  replica. This is the first recommendation in
  [Microsoft's OData client-performance guidance](https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/webservices/odata-client-performance)
  and is worth setting for reporting or synchronisation workloads.

  Opt-in on purpose: where a replica is genuinely used, replication lag means a read issued
  straight after a write may not observe it — correct for a sync job, wrong for a
  read-after-write flow, and the package cannot tell which yours is. The header is sent on
  `GET` **only**, because Microsoft documents that modification requests reject `ReadOnly`
  outright; attaching it to writes would turn every working write into an error. Streaming
  continuations and `$metadata` carry it too; the token request never does.

- **`BusinessCentralOptions.AcceptLanguage`** (default `null`): sets `Accept-Language`, which
  fixes the language of Business Central's error messages instead of letting it vary with
  tenant configuration. Sent on reads and writes alike.

- **Escape hatches for behaviour inferred from a single deployment.** Most of 2.0's polish came
  from one production consumer's feedback, which is why the package is as well-measured as it
  is — but a measurement from one tenant must never be the only thing a consumer can express.
  Every such default now has a registration-level override, and the defaults themselves are
  unchanged:

  - **`BusinessCentralOptions.DeriveSelect`** (default `true`) — the global counterpart to the
    per-query `SelectAll()`. A consumer whose entity classes are broad shared types rather than
    per-use projections can restore pre-2.0 behaviour in one line instead of at every call site.
  - **`BusinessCentralOptions.SchemaVersion`** (default `null`) sends `$schemaversion=` on
    every request, and **`Filter.In` now follows it**: at schema version 2.1 or later it
    renders the native `field in (…)`, otherwise the portable `or`-chain. One setting, no
    call-site changes.

    Microsoft documents the `in` operator as working
    [only in `$schemaversion=2.1`](https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/webservices/use-filter-expressions-in-odata-uris),
    and that is now confirmed live: the same query returns `501` without the parameter and
    `200` with it, with **byte-identical** response bodies to the `or`-chain control, across
    five entity sets spanning three kinds of published object. `$schemaversion=2.0` still
    returns `501`, so it is specifically 2.1. The `in` form is about half the encoded
    query-string width, which roughly doubles the keys that fit in one request.

    The rendering is resolved **when the request URL is built**, not when the filter is
    constructed — which is what makes it useful, since a chunked key lookup is almost always
    `.And(...)`-ed with something else and freezing the choice at construction would mean the
    automatic form never applied where it mattered.

    **One consequence to know about:** `ODataFilter.Value` and `ToString()` always give the
    portable `or`-chain, because a bare value has no endpoint to ask. On a 2.1 endpoint they
    therefore differ from what is sent. A test asserting on `Value` will keep passing while
    verifying something the wire no longer does — the same failure mode that produced this
    feature. Assert on `FakeBusinessCentral.Requests` instead; MIGRATION has the recipe.

    `ODataInStyle.Auto` is the new default; `OrChain` and `Native` pin one rendering per call,
    and `BusinessCentralOptions.InStyle` does the same globally. Forcing `Native` without the
    schema version produces a request the server answers with `501`, which is the caller's
    choice to make rather than one the package prevents.

  - **`BusinessCentralOptions.SchemaVersion` reaches every URL builder**, not only list
    queries. As first shipped it was emitted by one of six, so a consumer setting `"2.1"` read
    lists under one contract and did reads-by-key, writes, the company list, raw queries and
    `$metadata` under another — silently, and differently per method. A caller-supplied
    `$schemaversion` in a raw URL still wins.

### Fixed

- **A `400` caused by the derived `$select` now explains itself.** The projection is derived
  silently, so the server's message names the rejected column but cannot say why it was
  asked for — the caller never asked. The exception now states that the `$select` was derived
  from the entity type, names the implicated property when the server's message identifies
  one, and gives both remedies (`[JsonIgnore]` on the property, `SelectAll()` on the query).
  Applies only to fluent queries actually using a derived projection; explicit `Select(...)`,
  `SelectAll()`, `CountAsync` and the path-based API are untouched.

### Documentation

- **`$select` is case-*insensitive* on Business Central SaaS — the opposite of what alpha.6
  documented.** Measured against a live production tenant
  (`$metadata` probe round, live production tenant): `entry_No`,
  `Entry_No` and `ENTRY_NO` all return `200` on the same entity set, and the server answers
  in its own canonical casing regardless of what was requested. One consumer had 16 drifted
  wire names across 5 entity types running in production for months without a failure.

  Every shipped string asserting case-sensitivity is gone — the exception hint, the
  `EntitySelect` XML docs, MIGRATION and both READMEs. This mattered most in the hint, where
  the claim would have misdirected *every* real occurrence toward a cause that cannot
  produce the error, while the server's own message already gave the right answer. A test
  now pins the hint against the wording returning. Business Central on-premises runs a
  different OData stack and was not measured, so nothing now claims case-in*sensitivity*
  either; the honest position is that the package does not know.

- **Corrected an alpha.6 claim.** The derived-`$select` documentation said the feature
  surfaces latent drift rather than creating breakage. With casing eliminated above, there
  is exactly **one** cause and it is the created kind: a property that maps to no Business
  Central column at all used to bind as its default and cost nothing, and now fails the whole
  request with a `400` before deserialization is ever reached. MIGRATION and both READMEs
  name it as breakage, with the shape to watch for (a shared base class of system fields
  inherited by entity sets that do not all expose them).

  Field evidence that the risk is narrow: probed across 13 entity types / 118 derived
  columns in one production codebase, **zero** missing columns — including that exact
  inherited-system-fields shape, which existed on all five custom published pages.

- README: a *"URL length and bulk key lookups"* section covering both settings, the observer
  callback and the `or`-chain arithmetic.

- Testing package README: the projection validator, and what it does and does not prove.

- **README gained a `Performance` section**, mapping
  [Microsoft's OData client-performance guidance](https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/webservices/odata-client-performance)
  onto the package: the two new headers, what the package already does (derived `$select`,
  server-driven paging, no `$skip` loop), and what is still the caller's job — pairing `Top`
  with `OrderBy` for stable ranking, filtering on `LastModifiedOn` for history windows, and
  limiting the set around an expensive `$expand`.

  It also records independent support for the derived `$select` default: Microsoft notes that
  Business Central supports table extensions, so *"If you omit the `$select` clause, your query
  will return all fields from the table, including fields from other extensions"* — the
  projection is a documented performance measure, not only an ergonomic one.

- **FlowFilters are documented.** Business Central exposes a page's FlowFilters as ordinary
  `Edm.String` properties named `*_Filter`; they do not filter rows but parameterise the
  FlowField calculations on the rows returned. They work through the normal filter API, which
  is not obvious from either side.

### Deferred to 2.1

- **Deriving `$select` inside `$expand`** ([#55](https://github.com/KralGmbh/Dynamics365-BusinessCentral/issues/55)).
  Microsoft recommends it and the package does not do it, so an expand still returns every
  column of the expanded entity. Held back because it is the same silent-narrowing risk as the
  root projection, and one such change per release is enough.
- **OData transactional `$batch`** ([#56](https://github.com/KralGmbh/Dynamics365-BusinessCentral/issues/56)).
  The real gap is atomicity rather than request count: a business operation spanning several
  writes currently has no transactional boundary.

- **README gained a `Scope` section.** The package targets the OData v4 **published-pages**
  endpoint on Business Central SaaS and builds URLs around `Company('NAME')`. The standard
  `/api/v2.0` REST API uses a different shape (`companies({guid})/…`) and is not supported;
  on-premises is untested. Stating this is worth more than it costs: a consumer who learns it
  from the README is inconvenienced, one who learns it after adopting is gone.

## [2.0.0-alpha.6] - 2026-07-30

**Prerelease.** The consumer-ergonomics round (F1/F2 from the feature-request
round), shipped ahead of the
consumer's fluent-builder migration.

### Added

- **Builder-inferred filters (F1).** `Query<T>().Where(f => f.Equals(x => x.Status, "Open")
  .And(f.GreaterThan(x => x.Amount, 100)))` — `IFilterBuilder<T>` mirrors every `Filter`
  operator with the entity type fixed, forwarding to the existing typed overloads so
  rendering is identical. The static form remains.
- **`SelectAll()`** on the fluent builder: requests every column, suppressing the derived
  projection below.
- **`BusinessCentralOptions.RequestTimeout`** (nullable, default null = `HttpClient`'s
  100s): per-attempt timeout for the data client on the DI path — no more re-registering
  the named client just to set a timeout. Timeouts surface as
  `BusinessCentralConnectionException` and retry under the normal rules.

### Changed

- **The fluent builder derives `$select` from the entity type (F2)** when neither
  `Select(...)` nor `SelectAll()` is used: the settable scalar properties — the columns the
  type can actually hold — resolved through the same rules as filters and deserialization,
  ordinal-sorted for deterministic URLs. Navigation properties, get-only computed
  properties, `[JsonIgnore]` and `@`-annotations are excluded; `CountAsync` sends no
  `$select`. This can only **narrow** what is requested, so existing deserialization keeps
  working — but note `$select` is case-sensitive server-side: latent `[JsonPropertyName]`
  casing drift that deserialization tolerated now fails loudly. Path-based `QueryAsync` is
  unchanged.

  > **Superseded.** The case-sensitivity claim above was measured **false** against a live
  > Business Central SaaS tenant and is corrected in `2.0.0-alpha.7`. Kept here as the
  > historical record of what alpha.6 shipped; do not act on it.

### Documentation

- README: builder-form `Where` as the leading example; derived-`$select` explanation; new
  *"Mapping exceptions in message-level retry policies"* section (Wolverine/MassTransit/
  Polly must key on `BusinessCentralConnectionException`/`BusinessCentralThrottledException`
  or `IsTransient` — never `HttpRequestException`, which the client no longer lets escape).

## [2.0.0-alpha.5] - 2026-07-30

**Prerelease.** The final validation build before 2.0.0 stable: server-driven paging from
the round-four live-tenant measurements, the pre-stable hardening batch, and the closure
of the last *Unverified* item. Every open item from four review rounds is now shipped,
documented, or explicitly scheduled for 2.1.

### Changed

- **Auto-paging is server-driven** (`QueryAllAsync`, `QueryStreamAsync`, fluent
  `StreamAsync`/`ToAllAsync`), based on live-tenant measurement
  (nextLink measurement round): by default no page size
  is sent, Business Central pages at its own configured Max Page Size (20,000 online) and
  continuation follows `@odata.nextLink` — an opaque `$skiptoken` cursor immune to the
  row-shift hazards of `$skip` offset paging, which is gone (a caller-set `WithSkip` still
  applies, to the first request). A full 118k-row sweep drops from 119 round trips to 6.
  The package no longer ships a page-size constant; `WithPageSize`/fluent `PageSize` now
  request smaller server pages via `Prefer: odata.maxpagesize` (clamped by the server)
  instead of issuing `$top` round trips. `WithTop` is unchanged: a pure result cap, sent
  as `$top` and enforced client-side across continuations. Single-page reads never send
  the page preference — it would silently truncate them.

### Added

- **`BusinessCentralOptions.MaxPageSize`** (nullable, default null): the registration-level
  page preference for streaming reads, overridable per query with `WithPageSize`. Null
  defers entirely to the server's configuration.

### Fixed

- **A throwing observer can no longer break requests.** `IBusinessCentralObserver`
  callbacks are isolated: diagnostics are best-effort, so a bug in an observer no longer
  turns a successful request into a failure or masks the real server error.
- **Caller-requested cancellation is no longer reported to the observer as a request
  failure** — ordinary shutdowns stop putting noise in error metrics. Timeouts (which are
  not caller-requested) are still reported.
- **The manual-construction constructor documents its trade-off**: a privately created,
  per-instance token cache and shared token/data HTTP traffic — construct once and reuse,
  or prefer `AddBusinessCentral`.

## [2.0.0-alpha.4] - 2026-07-30

**Prerelease.** Incorporates the first live-tenant validation round: one behavioural fix
found only by hitting a real tenant, plus field-verified documentation. Remaining before
stable: the annotated *Unverified* items under [2.0.0-alpha].

### Fixed

- **`Filter.In` works against real Business Central tenants.** It rendered the OData `in`
  operator, which Business Central only accepts with `$schemaversion=2.1` — on a stock
  endpoint the server answers `BadRequest_MethodNotImplemented` (verified live). It now
  renders an equivalent same-field `or`-chain (`(f eq v1) or (f eq v2) …`), which is
  supported on every schema version. Empty-collection semantics are unchanged (`false`);
  a single value collapses to a plain `eq`.
- **Documented a Business Central limitation on `.Or(...)`**: `or` between filters on
  *different* fields has no AL equivalent and is rejected by the server. Same-field `or`
  and `.And(...)` are unaffected.
- **Documented null-vs-blank semantics on `Filter.IsNull`/`IsNotNull`** (live-tenant
  finding): AL text fields cannot be null, and BC maps `eq null` onto "is blank" — so
  `IsNull` matches empty strings and `IsNotNull` excludes them, unlike the equivalent
  LINQ predicate.
- **Documented date modelling** (live-tenant finding): BC `Edm.Date` fields are date-only
  on the wire, with `0001-01-01` as the unset sentinel — map them to `DateOnly`; a bare
  `DateTimeOffset` property fails deserialization on every row. The outbound side of the
  alpha.2 `DateOnly` filter fix was confirmed against a real `Edm.Date` field.
- **The Testing package README states what a transport fake cannot prove**: it verifies
  the OData you generate, not what Business Central accepts — operators still need one
  live verification.

## [2.0.0-alpha.3] - 2026-07-30

**Prerelease.** The final validation build before 2.0.0 stable: robustness fixes from a
second production-consumer review of the client internals, plus the companion testing
package. Still to be confirmed against a live tenant before stable: the *Unverified* list
under [2.0.0-alpha], plus a smoke test of a date filter.

### Added

- **`Dynamics365.BusinessCentral.Testing`**, a companion NuGet package. `FakeBusinessCentral`
  runs a real `BusinessCentralClient` over a scripted transport: consumers script responses
  (`EnqueuePage` with `nextLink`/`@odata.count`, `EnqueueEntity`, `EnqueueError` with
  `Retry-After`, `EnqueueNetworkFailure`, `EnqueueNoContent`) and assert the exact OData URL
  and body each call produced via `Requests`. Token acquisition is answered automatically;
  unscripted requests throw a message naming the request. Versioned in lockstep with the
  main package.

- **`Retry.JitterFactor`** (default `0.2`): every retry delay — computed backoff *and*
  honoured `Retry-After` — is spread by `random(0, delay × factor)`, capped by `MaxDelay`,
  so concurrent callers throttled at the same moment stop retrying in lockstep and
  re-throttling each other. The spread is additive only, because a `Retry-After` is a
  minimum wait. Set `0` for deterministic delays.

### Fixed

- **Token acquisition is retried.** The token request previously had no transient handling
  at all — a blip at `login.microsoftonline.com` failed every in-flight request at once,
  and following the README's resilience-composition recipe removed the only outer safety
  net. Token requests now honour the same `Retry` options as data requests (the
  `client_credentials` grant has no side effects, so replay is unconditionally safe),
  report `OnRequestRetrying`, and wrap network failures as
  `BusinessCentralConnectionException`. Credential failures (`400`/`401`) are not retried.
- **Concurrent `401`s no longer cascade token refreshes.** Invalidation is now
  compare-and-swap: a request that observed a stale token cannot clear a token that was
  already refreshed behind it.
- **`$expand` encoding handles unsafe characters.** Only spaces were escaped before, so an
  `&`, `#` or `+` inside a nested expand clause (e.g. `lines($filter=code eq 'A&B')`)
  silently truncated the query string. Structural expand syntax is preserved; everything
  else is percent-encoded.

### Changed

- The auto-paging state machine now exists once (shared by `QueryStreamAsync` and the
  fluent `StreamAsync`) instead of as two hand-synchronised copies. No behavioural change;
  both entry points are covered by the existing paging tests.

## [2.0.0-alpha.2] - 2026-07-29

**Prerelease.** The release candidate for 2.0.0 stable: everything below was driven by
reviewing the alpha and by feedback from a production consumer migrating to it. Still to
be confirmed against a live tenant before stable: the *Unverified* list under
[2.0.0-alpha], plus a smoke test of a date filter.

### Added

- **Default implementations on every `IBusinessCentralClient` member.** Hand-written test
  fakes now implement only the members they exercise; everything else throws
  `NotSupportedException` naming the member. Interface growth no longer breaks consumer
  builds. `FirstOrDefaultAsync` composes over `QueryAsync`, so fakes get it for free.
- **Single-entity reads on the path-based API.** `GetAsync<T>(path, key, select?)` fetches
  by systemId or alternate key and returns `null` on `404` — "does it exist" is a question,
  not an error. `FirstOrDefaultAsync<T>(path, filter?, select?)` sends `$top=1`.
- **`BusinessCentralHttpClients`** exposes the package's two `IHttpClientFactory` client
  names, so a global resilience handler (e.g. Aspire's `AddStandardResilienceHandler`) can
  exempt them instead of double-retrying — the README's *"Composing with an existing
  resilience pipeline"* section shows how.
- **Predicate properties on `BusinessCentralException`**: `IsNotFound`, `IsThrottled`,
  `IsValidation`, `IsAuth`, `IsConnectionFailure`. The subtypes are sealed siblings, so
  `catch (BusinessCentralServerException ex) when (ex.StatusCode == NotFound)` compiles but
  never matches; `when (ex.IsNotFound)` is the supported form.
- **`BusinessCentralField.Of<T>(selector)`** resolves a property selector to its wire name
  (`[JsonPropertyName]` first, then the camelCase policy), and **`EntityPath` is now
  public** — path-based consumers can delete hand-maintained field/path constants.

- **`BusinessCentralConnectionException`.** Connection-level failures and client-side
  timeouts — where no response was received at all — now surface as a
  `BusinessCentralException` subtype (`StatusCode` is `0`) instead of escaping as raw
  `HttpRequestException`/`TaskCanceledException`, so `catch (BusinessCentralException)`
  sees every failure the client produces. The original exception is preserved as
  `InnerException`. Cancellation via the caller's own token still throws
  `OperationCanceledException`, unwrapped.

### Changed

- **Network failures are retried.** Connection failures and client-side timeouts now go
  through the transient-retry budget under the same replay rules as `408`/`502`/`503`/`504`:
  idempotent methods are replayed, `POST` is held back unless
  `Retry.RetryPostOnTransientFailures` is set. Previously they bypassed retry entirely and
  failed on the first attempt.

### Fixed

- **`WithTop` is a result cap in `QueryAllAsync`/`QueryStreamAsync`**, matching its own
  documentation and the fluent builder's `Top()`. It was previously repurposed as the page
  size — `WithTop(10)` returned the entire collection in pages of 10 — and was silently
  ignored outright when `WithPageSize` was also set. Requests never overshoot the cap
  (`$top` shrinks to what is still wanted). Use `WithPageSize` to size round trips.
- **Kindless `DateTime` filter values are no longer shifted by the machine's timezone.**
  `DateTimeKind.Unspecified` — the kind of anything parsed from config or loaded from a
  database — was run through `ToUniversalTime()`, which assumes local time, so the same
  filter matched different rows depending on where the code ran. Unspecified is now taken
  to already be UTC. `Utc` and `Local` values are unaffected.
- **`DateOnly` and `TimeOnly` filter values produce valid OData literals**
  (`2026-07-29`, `13:45:30.0000000`). They previously fell through to a culture-formatted
  string such as `07/29/2026`, which Business Central rejects — notably breaking filters
  on date fields like `postingDate`.

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

Known gaps in this prerelease, to be confirmed before 2.0.0 stable
*(status as of the 2026-07-30 live-tenant validation against BC SaaS)*:

- ✅ **Verified 2026-07-30**: `GetCompaniesAsync` targets `{BaseUrl}/Company`. Against a
  live SaaS tenant all four properties bind — `Name`, `Display_Name`, `Id` and
  `Evaluation_Company` were all populated. The "best-effort and null" caveat below applies
  only if an endpoint omits them, which the live tenant did not.
- ✅ **Verified 2026-07-30**: `Prefer: return=representation` on writes. Measured via a
  no-op `PATCH` against a live tenant: the `ODataV4` page endpoint returned `200` with the
  full entity **both with and without the header** — these endpoints always echo on
  `PATCH`, so the header is redundant there (and harmless). The `204` path remains as a
  safety net; `POST` echo behaviour was left untested by choice (it would require creating
  a real record).
- ⚠️ **Inconclusive 2026-07-30**: server-driven `@odata.nextLink` paging. A 118k-row query
  against an `ODataV4` published-page endpoint did not produce server-driven paging;
  those endpoints may not emit `nextLink` at all, unlike `/api/v2.0`. The nextLink
  handling remains covered by tests but unobserved in the wild.

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
