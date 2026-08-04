# Migrating from 1.0 to 2.0

2.0 fixes several defects and reworks the consumer-facing API. Most existing code keeps
compiling — the changes that need your attention are mostly **silent behavioural** ones,
so read that section even if the build is green.

---

## 1. Changes that can break the build

### `IBusinessCentralClient` gained members

New members: `Company`, `ForCompany`, `Query<T>()` (two overloads), `QueryStreamAsync<T>`,
`GetCompaniesAsync`, `GetAsync<T>`, `FirstOrDefaultAsync<T>`, and two-generic
`PostAsync`/`PatchAsync`/`PutAsync`.

Mocking libraries (Moq, NSubstitute, FakeItEasy) absorb this automatically. Hand-written
test fakes keep compiling too: **every interface member has a default implementation**, so
a fake only implements the members it actually exercises. An unimplemented member throws
`NotSupportedException` naming itself, and `FirstOrDefaultAsync` composes over `QueryAsync`
so fakes get it for free.

Before extending a hand-written fake, consider the companion package
`Dynamics365.BusinessCentral.Testing`: its `FakeBusinessCentral` runs a **real** client
over scripted responses, so tests can assert the exact OData a call produced — the thing
a fake or mock can never verify. See the README's *Testing* section.

### Nothing else, in practice

`QueryAsync`, `QueryAllAsync`, `QueryRawAsync`, `PostAsync<T>`, `PatchAsync<T>`,
`PutAsync<T>`, `DeleteAsync`, `Filter.*` and `.And/.Or/.Not` all keep their signatures.
Existing single-generic write calls — including `PostAsync<dynamic>(path, payload, ct)` —
still resolve to the same overload; generic arity disambiguates.

---

## 2. Silent behavioural changes — read this section

### `204 No Content` on a write is now success

| | 1.0 | 2.0 |
| --- | --- | --- |
| `POST`/`PATCH`/`PUT` returning 204 | threw `BusinessCentralServerException` | returns successfully |

Writes now send `Prefer: return=representation`, so 204 is less likely. When it does
happen, the single-generic overloads return **the payload you sent**; the new two-generic
overloads return **`default`**.

**If you have a `catch` that relied on 204 failing, it will no longer fire.** Conversely,
code that inspects the returned object may now receive the echoed payload rather than a
deserialized response:

```csharp
// This now yields the anonymous payload on 204, not a JsonElement:
object? raw = await client.PostAsync<dynamic>(path, new { … });
if (raw is not JsonElement element) { /* now reachable on 204 */ }
```

The fix is the new overload — see §4.

### `429` is no longer a `BusinessCentralServerException`

Throttling now raises `BusinessCentralThrottledException`. It is a **sibling** of
`BusinessCentralServerException`, not a subclass, so:

```csharp
catch (BusinessCentralServerException ex) { … }   // no longer sees 429
catch (BusinessCentralException ex) { … }          // sees everything
```

> **Worth auditing while you are here.** This applies to the other subtypes too, and always
> did: `BusinessCentralServerException` never caught `400`, `401`, `403` or `404` either —
> those are `Validation`, `Auth`, `Auth` and `NotFound`, all sealed siblings. A guard like
> `catch (BusinessCentralServerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)`
> can never match. Catch `BusinessCentralNotFoundException`, or use the predicates on the
> base type — `catch (BusinessCentralException ex) when (ex.IsNotFound)` — which exist
> precisely because this trap compiles.

### Transient failures are retried automatically

`429`, `408`, `502`, `503` and `504` are now retried (3 attempts by default), honouring
`Retry-After`. Calls therefore take longer before surfacing a failure, and fewer transient
errors reach your code. Delays are jittered by default (`Retry.JitterFactor`, `0.2`) so
concurrent callers do not retry in lockstep — set it to `0` if your tests assert exact
timings. Token acquisition follows the same retry options; bad credentials (`400`/`401`
from the token endpoint) are never retried.

**Writes are not blindly replayed.** A `429` is rejected before processing so replay is
always safe; the others are ambiguous, so a `POST` is *not* retried on them:

| Method | `429` | `408`/`502`/`503`/`504` |
| --- | --- | --- |
| `GET`, `PUT`, `DELETE` | retried | retried |
| `PATCH` | retried | retried — this client sends absolute values, so replay converges |
| `POST` | retried | **not** retried |

Connection-level failures — a reset connection, a DNS error, the `HttpClient` timeout —
follow the same rules as the ambiguous statuses: idempotent methods are retried, `POST` is
not. They also now surface as `BusinessCentralConnectionException` (`StatusCode` is `0` —
no response was received) instead of a raw `HttpRequestException` or
`TaskCanceledException`. **A `catch (HttpRequestException)` around client calls no longer
fires**; catch `BusinessCentralConnectionException` or the base type, and find the original
exception on `InnerException`. Cancelling through your own `CancellationToken` still throws
`OperationCanceledException`, unwrapped.

If you already have an outer retry policy (Polly, Wolverine, a message broker), the two now
compose multiplicatively. The package's HTTP clients are addressable by name
(`BusinessCentralHttpClients.Client` / `.Token`), so the preferred fix is exempting them
from the outer handler and keeping this retry — see the README section *"Composing with an
existing resilience pipeline"*. Alternatively, lower `Retry.MaxAttempts` or disable it:

```csharp
options.Retry.Enabled = false;   // 1.0 behaviour — but a generic outer handler replays POST
```

### `QueryAllAsync` may return more rows than before

It now follows `@odata.nextLink`. Where Business Central applied a server-side page cap
below the requested `$top`, 1.0 stopped early and silently truncated. Reconciliation logic
that assumed the old result size should be re-checked.

### Auto-paging is server-driven

1.0 (and the 2.0 alphas) paced streaming reads by sending `$top`/`$skip` per round trip.
2.0 stable sends **no page size by default**: Business Central pages at its own configured
Max Page Size (20,000 online; the `ODataServicesMaxPageSize` server setting on-premises)
and drives continuation via `@odata.nextLink`, an opaque `$skiptoken` cursor — verified
against a live SaaS tenant.

What this changes in practice:

- **Fewer, larger responses.** A full sweep of a 118k-row entity set drops from 119
  round trips (at the old invented default of 1,000) to 6 — but each response is up to
  20,000 rows. If per-response size matters (memory, timeouts), set
  `BusinessCentralOptions.MaxPageSize` or a per-query `WithPageSize(n)`; the value is sent
  as `Prefer: odata.maxpagesize` and clamped by the server.
- **No more offset paging.** `$skip` no longer advances between pages, so concurrent
  inserts/deletes can no longer shift rows between requests. A caller-set `WithSkip(n)` is
  still honoured — as the starting offset of the first request.
- **`WithTop` is unchanged**: a pure result cap, sent as `$top` and enforced client-side.

### `QueryAllAsync` with `WithTop` may return far fewer rows

The opposite direction: `WithTop(n)` was 1.0's page size and did not limit results; it is
now a result cap, as documented. A call like `QueryAllAsync(..., o => o.WithTop(500))`
that used to fetch *everything* in pages of 500 now returns at most 500 rows. Replace it
with `WithPageSize(500)` to keep the old behaviour — see §3.

### The fluent builder derives `$select` from the entity type

A `Query<T>()` call with no explicit `Select(...)` used to request **every column**; it now
sends a `$select` derived from `T`'s settable scalar properties — the columns the type can
actually hold. `.SelectAll()` restores the full row for deliberately partial entity types.
The path-based `QueryAsync(select:)` is unchanged.

Narrowing the *response* is safe: anything that deserialized before still deserializes.
Narrowing the *request* is not, and earlier wording here obscured that by talking only
about deserialization — **the request can now fail before deserialization is reached.**
Nothing in the package can consult your tenant's schema, so every derived name is validated
by the server, and a name it does not recognise fails the whole query with a `400`.

**One way an upgrade can break:** a property that maps to **no Business Central column at
all**. It used to bind as its default and cost nothing; it now enters `$select` and draws a
`400` naming the field. This is breakage this release creates, not latent drift it
surfaces. Say a base class contributes `SystemCreatedAt`/`SystemModifiedAt` to a dozen
entities and only some published pages expose those columns: nothing in the model says so,
and today those queries work. After upgrading they `400`. An inherited base class of system
fields is the exact shape to check.

> **A non-public setter counts as settable when it carries `[JsonInclude]`.** Such a property
> is populated by `System.Text.Json`, so it is a real column and now enters the derived
> `$select`. Earlier alpha.7 builds skipped it, which left it silently empty on every row
> while the request still returned `200`. If one of these maps to no Business Central column,
> it can now draw the same `400` as any other derived name — the remedy is the same
> `[JsonIgnore]` or `SelectAll()`.

> **Casing is not a second cause.** Releases up to `2.0.0-alpha.7` warned here that
> `$select` is case-sensitive server-side and that a drifted `[JsonPropertyName]` would
> start failing. **That was measured false.** Against a live Business Central SaaS
> production tenant, `entry_No`, `Entry_No` and `ENTRY_NO` all returned `200` on the same
> entity set, and the server answers in its own canonical casing regardless of what was
> requested. One consumer had 16 drifted wire names across 5 entity types in production for
> months without a single failure. Casing drift needs no action. (Business Central
> on-premises runs a different OData stack and was not measured, so this is "not
> case-sensitive where measured", not a guarantee.)

**Check this, and keep checking it.** The Testing package ships a validator:

```csharp
await BusinessCentralMetadata.AssertProjectionsResolveAsync(client, typeof(Item).Assembly);
```

It fetches `$metadata`, derives the `$select` for every `[BusinessCentralEntity]` type, and
fails listing **every** name that matches no column. One assertion, no production risk.

Run it as a one-off before upgrading if you like, but it earns its place as a standing
integration test, because **your unit suite cannot catch this**. Mocks of
`IBusinessCentralClient` do not validate `$select`, and neither does `FakeBusinessCentral` —
a transport fake proves what OData you generate, never what your tenant accepts. Without
this check the sequence is *upgrade → tests green → production incident*. And the failure is
introduced by **adding a property**, an edit nobody associates with a query breaking, which
is why a check that runs on every build beats one you perform once.

Field result from the one production codebase measured before this shipped: 13 entity types,
118 derived columns, **zero** missing — including the inherited `SystemId` /
`SystemCreatedAt` / `SystemModifiedAt` shape above, which existed on all five custom
published pages. Read that as *the failure is not ubiquitous*, not *the failure is rare*:
those classes were written per use as BC projections, and most of those columns were already
being named in explicit `select:` lists that had run in production for months. The shapes
most at risk look different — one broad shared class with convenience properties, a
speculatively added field, a class that outlived a schema change.

**Remedies**, both already present: `[JsonIgnore]` on a property drops it from the
projection permanently; `.SelectAll()` on a query sends no `$select` at all. Since
`2.0.0-alpha.7` the exception itself says the projection was derived, names the implicated
property, and points at both — you should not have to reason this out from a bare server
message.

### `Filter.None` no longer sends a request, and `Filter.All` composes away

New in `2.0.0-alpha.7`. Business Central's documented filter set is field-and-operator only —
there is no boolean-literal construct — so neither `$filter=false` nor `$filter=(true) and (…)`
is something a tenant can be asked. Earlier alpha.7 builds sent both:

| Expression | Was sent as | Now |
| ---------- | ----------- | --- |
| `Filter.In(field, [])` → `Filter.None` | `?$filter=false` | no request; empty result, `Count` `0` |
| `Filter.All.And(x)` | `?$filter=(true) and (x)` | `?$filter=x` |
| `Filter.None.Or(x)` | `?$filter=(false) or (x)` | `?$filter=x` |
| `Filter.All` alone | *(already omitted)* | unchanged |

**What changes for you.** A query whose filter reduces to `Filter.None` completes without a
round trip, so a handler that counted requests sees one fewer, and `FakeBusinessCentral` records
none. That is the point: the empty result is the correct answer, and it is now reached without
asking the server a question it has no way to answer.

`ODataFilter.Value` is unchanged — `Filter.None.Value` is still `"false"`. Only what reaches
the wire differs, so **tests asserting on `Value` still pass while asserting nothing about the
request**; the same trap as the section below. Assert on `FakeBusinessCentral.Requests`.

### `ODataFilter.Value` can differ from what goes on the wire

New in `2.0.0-alpha.7`, and only when `SchemaVersion` is `2.1` or later.

`Filter.In` defaults to `ODataInStyle.Auto`, whose rendering is decided by the client when it
builds the request URL — that is what lets one setting switch every membership filter to the
native `in` operator. A bare `ODataFilter` has no endpoint to ask, so `Value` and `ToString()`
always give you the portable `or`-chain:

```csharp
services.AddBusinessCentral(o => o.SchemaVersion = "2.1");

var filter = Filter.In<Item>(i => i.No, ["A", "B"]);

filter.Value;   // "(no eq 'A') or (no eq 'B')"   — always
// the request actually sent:  ?$filter=no in ('A','B')&$schemaversion=2.1
```

**If you have tests asserting on `Value` or `ToString()`, they now verify something the wire
may not do.** They will not fail — which is the problem. This is the same failure mode as the
`in`-operator finding that produced this feature: a test that passes against a fake while the
tenant rejects the real request.

Assert on the request instead. `FakeBusinessCentral` records what was actually sent:

```csharp
using var bc = new FakeBusinessCentral(o => o.SchemaVersion = "2.1");
bc.EnqueuePage<Item>();

await bc.Client.Query<Item>().Where(f => f.In(i => i.No, ["A", "B"])).ToListAsync();

Assert.Contains("no in ('A','B')", bc.Requests.Single().DecodedPathAndQuery);
```

Nothing changes if you do not set `SchemaVersion`, or if you pin a rendering with
`ODataInStyle.OrChain` / `.Native` — in those cases `Value` and the wire agree.

### A long `$filter` is now refused client-side instead of failing opaquely

New in `2.0.0-alpha.7`. `BusinessCentralOptions.MaxQueryStringLength` defaults to `8000`
characters; a request that builds a longer **query string** throws
`BusinessCentralUrlTooLongException` before it is sent.
`QueryStringLengthWarningThreshold` (default `6000`) raises the new
`IBusinessCentralObserver.OnUrlLengthWarning` while still sending the request.

> Earlier alpha.7 builds threw `ArgumentException` here. If you wrote a handler against that,
> re-key it: the exception now derives from `BusinessCentralException` like every other failure
> the client produces, so `catch (BusinessCentralException)` sees it. Match `ex.IsUrlTooLong`
> to single it out; `StatusCode` is `0` (nothing was sent), which it shares with
> `BusinessCentralConnectionException`, so do not key on the status alone.

The limit is on the query string, not the whole URL, because that is what Business Central's
gateway actually limits — measured at **8,099** accepted characters, invariant across two
environments whose full URLs differed. Past the server's own ceiling you get
`414 URI Too Long`, which is not cryptic; the value of failing client-side first is that the
message names the length, the limit, the `or`-clause count and `Filter.In` as the likely
cause.

This only bites bulk key lookups, and mainly through `Filter.In`: it falls back to an
`or`-chain because Business Central gates the OData `in` operator on schema version 2.1, and
each encoded `(no eq 'EBH00000') or ` costs 38 characters against 17 for the `'EBH00000',` it
replaces.

**The cheapest fix is usually to stop paying for the workaround.** If your endpoint serves
schema version 2.1, one setting halves it:

```csharp
services.AddBusinessCentral(o => o.SchemaVersion = "2.1");
```

That is all. `Filter.In` defaults to `ODataInStyle.Auto`, which reads the configured schema
version at request-build time — no call-site changes. Do **not** also pass
`ODataInStyle.Native`: pinning the rendering means that if the schema version is ever removed
or lowered the filters keep emitting `in` and start returning `501`, which is exactly what
`Auto` exists to prevent.

If you have a working query above `8000` characters of query string, raise
`MaxQueryStringLength` or set it to `null`. Server-issued `@odata.nextLink` continuations are
never checked.

### Message-level retry policies must re-key their exceptions

An outer retry policy (Wolverine, MassTransit, Polly) keyed on `HttpRequestException`
**silently stops matching** in 2.0: the client wraps transport failures in
`BusinessCentralConnectionException`. Match that type (or `ex.IsTransient`), and give
`BusinessCentralThrottledException` a slower curve — the client has already honoured
`Retry-After` in-process. See the README's *"Mapping exceptions in message-level retry
policies"* table.

### Kindless `DateTime` filters are no longer shifted by the machine's timezone

1.0 passed every `DateTime` through `ToUniversalTime()`, which treats
`DateTimeKind.Unspecified` — anything parsed from config or loaded from a database — as
*local* time. The same filter matched different rows depending on the server's timezone.
2.0 takes Unspecified as already UTC. If you relied on the local-time interpretation,
apply `DateTime.SpecifyKind(value, DateTimeKind.Local)` before filtering.

### `Exception.Message` is now a single line

1.0 baked the status, URL and **entire response body** into `Message`. 2.0 keeps it to one
line; `ResponseBody`, `RequestUrl`, `ODataErrorCode`, `CorrelationId` and `RetryAfter` are
properties, and `ToString()` renders everything.

Logging `ex` is unaffected. Code that stores or displays `ex.Message` will see shorter,
cleaner text.

### Access tokens are shared across injections

1.0 kept the token cache on the client instance, but typed `HttpClient`s are transient — so
every resolution of `IBusinessCentralClient` triggered a fresh token request. 2.0 moves the
cache to a singleton. Expect a large drop in calls to your identity provider. No code
change required.

### Two bugs whose fixes change URLs

- `QueryRawAsync("salesOrders?$top=5")` previously percent-encoded the query string into the
  path (`salesOrders%3F%24top%3D5`). It now works as documented.
- Alternate keys such as `No='1000'` were mangled to `No%3D%271000%27`. They now survive.

If you built workarounds for either, remove them.

---

## 3. Renames and deprecated shapes

### `WithTop` vs `WithPageSize`

In 1.0's `QueryAllAsync`, `WithTop(n)` meant *page size*, not a result limit — it paged
through the entire collection `n` rows at a time. In 2.0 `WithTop` is what it says: a
result cap, everywhere. `QueryAllAsync(..., o => o.WithTop(10))` now returns at most 10
rows. Use `WithPageSize(n)` for the old intent — though the mechanism differs: it now
requests server pages of at most `n` rows via `Prefer: odata.maxpagesize` rather than
issuing `$top=n` round trips (see *Auto-paging is server-driven* in §2):

```csharp
// 1.0: WithTop(500) fetched everything, 500 rows per round trip. 2.0 equivalent:
await client.QueryAllAsync<SalesOrder>("salesOrders", options: o => o.WithPageSize(500));
```

### Chained ordering

`OrderByAsc(a).OrderByAsc(b)` silently discarded `a` in 1.0. `OrderByAsc`/`OrderByDesc`
still *replace* the ordering; use `ThenByAsc`/`ThenByDesc` to append:

```csharp
o.OrderByDesc("amount").ThenByAsc("no")   // $orderby=amount desc,no asc
```

### Observer

`OnTokenRefreshed` now fires only on a real refresh. Cache hits raise
`OnTokenServedFromCache`, and retries raise `OnRequestRetrying`. Both are default interface
methods, so existing observers keep compiling.

---

## 4. Recommended clean-ups

### Replace `dynamic` writes with the two-generic overloads

`PostAsync<T>` forced the request and response to be the same type, which pushed callers
into `dynamic` plus a runtime type check. Before:

```csharp
object? raw = await client.PostAsync<dynamic>(path, payload, ct);
if (raw is not JsonElement element) throw new …;
if (!TryGetSystemId(element, out var systemId)) throw new …;
```

After:

```csharp
var created = await client.PostAsync<object, CreatedRow>(path, payload, ct);

// null means Business Central applied the write but returned no representation.
if (created is null) throw new …;

var systemId = created.SystemId;
```

`TResult` is unconstrained, so `JsonElement` still works if you have no model:

```csharp
var element = await client.PostAsync<object, JsonElement>(path, payload, ct);

// Value types cannot be null — a 204 yields default(JsonElement).
if (element.ValueKind == JsonValueKind.Undefined) { /* created, not echoed */ }
```

Failures still throw; an empty result only ever means "created, not echoed".

### Simplify configuration

Only `TenantId`, `ClientId`, `ClientSecret` and `Company` are required. `BaseUrl` and
`TokenEndpoint` default to the SaaS endpoints and resolve `{tenant}` and `{environment}`:

```csharp
services.AddBusinessCentral(options =>
{
    options.TenantId     = …;
    options.ClientId     = …;
    options.ClientSecret = …;
    options.Company      = "CRONUS AG";
    options.Environment  = "UAT3";     // instead of a hand-built BaseUrl
});
```

Or bind a section directly:

```csharp
services.AddBusinessCentral(builder.Configuration.GetSection("BusinessCentral"));
```

> In 1.0 the `{tenant}` placeholder shown in the README was **never substituted in
> `BaseUrl`** — only `{TenantId}` in `TokenEndpoint` was. If you worked around this by
> hard-coding the URL, you can now use the placeholder form. Validation rejects any
> unsubstituted `{…}` that remains.

### Adopt typed queries incrementally

Field-name strings still work. The typed form resolves names through the same
`JsonSerializerOptions` used for deserialization, so filters cannot drift from the model:

```csharp
var orders = await client.Query<SalesOrder>()          // path from [BusinessCentralEntity]
    .Where(Filter.Equals<SalesOrder>(o => o.Status, "Open"))
    .OrderByDescending(o => o.Amount)
    .ThenBy(o => o.No)
    .ToListAsync();
```

There is no deprecation on the path-based API; migrate at your own pace, or not at all.

---

## 5. Checklist

- [ ] Hand-written `IBusinessCentralClient` fakes keep compiling (default interface
      methods); implement any newly-exercised member, delete `NotSupportedException` stubs
- [ ] Audit `catch (BusinessCentralServerException)` — it does not see `400`/`401`/`403`/`404`/`429`
- [ ] Decide on retry: keep it, tune `MaxAttempts`, or `Retry.Enabled = false`
- [ ] Re-check any logic that assumed a `204` write failed
- [ ] Re-check result-size assumptions around `QueryAllAsync`
- [ ] Replace `WithTop` used as a page size with `WithPageSize`
- [ ] Check `DateTime` filter values for `Kind=Unspecified` semantics (now read as UTC)
- [ ] Replace `dynamic` writes with the two-generic overloads
- [ ] **Add `BusinessCentralMetadata.AssertProjectionsResolveAsync` as an integration test**
      — a property with no matching column now fails the whole query, and nothing else in a
      normal test suite detects it
- [ ] Check inherited base classes of system fields against the entity sets that inherit
      them; not every published page exposes them
- [ ] Re-check chunk sizes on bulk key lookups against `MaxQueryStringLength` (an `or`-chain costs
      ~4× per key what `in (...)` would)
- [ ] Optionally simplify configuration and drop hand-built `BaseUrl`
