# Consumer feedback: Bastion

Improvement proposals for **Dynamics365.BusinessCentral**, derived from migrating a large
production consumer (Bastion) from `1.0.0` to `2.0.0-alpha` on 2026-07-29.

This document is written to be self-contained: an agent working in this repository does not
need access to the Bastion codebase. All supporting evidence is quoted inline.

**How to use this file.** Each proposal is independent and issue-shaped: problem → evidence →
proposal → acceptance criteria. Implement in priority order, or split into separate issues.
Nothing here is a bug report against the package — everything below is behaving as designed.
The argument is that some of those designs push avoidable work onto consumers.

---

## Context: how Bastion uses this package

| Property | Value |
| --- | --- |
| Consumer | Bastion — event-sourced modular monolith, live in production since 2026-05-03 |
| Package version | `2.0.0-alpha` (tag `v2.0.0-alpha` = commit `1d6dc8f`) |
| Target framework | `net10.0` |
| Call style | **100% path-based** (`QueryAsync`, `QueryAllAsync`, `PostAsync`, `PatchAsync`, `DeleteAsync`). The typed `Query<T>()` builder is not used anywhere. |
| Companies | Single (`KRAL AG`). `ForCompany` / `GetCompaniesAsync` unused. |
| Observers | **None.** `IBusinessCentralObserver` is not implemented anywhere. |
| Adapters | 6 classes wrapping the client across 4 bounded contexts |
| Resilience | `Microsoft.Extensions.Http.Resilience` `AddStandardResilienceHandler` applied globally via .NET Aspire `ConfigureHttpClientDefaults` |

The migration result is the key datum for this document:

> **18 compile errors. All 18 were in two hand-written `IBusinessCentralClient` test fakes.
> Zero production code required changes to compile.**

The package's breaking-change surface, in practice, was entirely a *testing* surface.

---

## Summary

| ID | Proposal | Priority | Est. effort |
| --- | --- | --- | --- |
| P1 | Default interface methods on `IBusinessCentralClient` | **High** | Low |
| P2 | Ship a `Dynamics365.BusinessCentral.Testing` package | **High** | Medium |
| P3 | Auto-chunked `In` / key-set fetch | **High** | Medium |
| P4 | Native OpenTelemetry (`ActivitySource` + `Meter`) | Medium | Medium |
| P5 | Make the HTTP client names public | Medium | Trivial |
| P6 | Single-entity fetch on the path-based API | Medium | Low |
| P7 | Predicate helpers on `BusinessCentralException` | Medium | Trivial |
| P8 | Public field-name resolution + selector-based `$select` on the path API | **High** | Low |

If only one is implemented, implement **P2** (with **P1** as its prerequisite). That is the
one place where the package currently *creates* recurring work for consumers rather than
absorbing it.

---

## P1 — Default interface methods on `IBusinessCentralClient`

**Priority: High · Effort: Low · Breaking: No**

### Problem

Every addition to `IBusinessCentralClient` is a source-breaking change for any consumer that
hand-writes a test double. 2.0 added nine members, and that alone accounted for 100% of the
build breakage in a ~200k-line consumer.

Mocking libraries absorb this. Hand-written fakes do not — and consumers write hand-written
fakes when the behaviour they need is stateful enough that `Moq` setups become unreadable
(see P2 for why they get unreadable here specifically).

### Evidence

The nine members added in 2.0, each of which had to be stubbed by hand in two Bastion test
classes purely to restore compilation:

```
Company, ForCompany, Query<TEntity>(), Query<TEntity>(string), GetCompaniesAsync,
QueryStreamAsync, PostAsync<TPayload,TResult>, PatchAsync<TPayload,TResult>,
PutAsync<TPayload,TResult>
```

None of them were called by the code under test. All were stubbed as
`throw new NotSupportedException()`.

### Proposal

Give every member a default implementation of `=> throw new NotSupportedException(...)`.

This precedent already exists in the package and is documented in the 2.0 changelog:

> `OnTokenServedFromCache` and `OnRequestRetrying` observer events, **both default interface
> methods so existing observers keep compiling**.

Apply the same reasoning to the client interface. The members a fake cannot meaningfully
implement are exactly the members it does not want to implement.

Note the C# detail for the two-generic overloads: an unconstrained `TResult` in a default
implementation of an interface member that returns `TResult?` needs `where TResult : default`
on the implementing signature.

### Acceptance criteria

- [ ] Every member of `IBusinessCentralClient` has a default implementation that throws
      `NotSupportedException` with a message naming the member.
- [ ] A test fixture implementing *only* `QueryAsync<T>` compiles and runs.
- [ ] A regression test asserts that a minimal implementer compiles (e.g. a fake in the test
      project that implements one member and nothing else).
- [ ] MIGRATION.md's "hand-written test fake will not compile" warning can be deleted.

---

## P2 — Ship a `Dynamics365.BusinessCentral.Testing` package

**Priority: High · Effort: Medium · Breaking: No**

### Problem

Two distinct costs, both borne by every consumer:

1. **Fakes must be hand-written**, so interface growth breaks builds (P1 mitigates, does not
   eliminate).
2. **The generated OData is unassertable.** A consumer can verify *that* a query happened but
   not *what it asked for*. Filter construction, `$select` field names, casing and paging are
   the highest-risk part of using an OData client, and they are the part consumers cannot
   test.

### Evidence

Bastion's mock setups, repeated roughly forty times in a single test file:

```csharp
clientMock.Setup(c => c.QueryAsync<ProductionOrderEntity>(
    It.IsAny<string>(),
    It.IsAny<ODataFilter?>(),
    It.IsAny<Action<QueryOptions>?>(),
    It.IsAny<IEnumerable<string>?>(),
    It.IsAny<CancellationToken>()))
    .ReturnsAsync([...]);
```

Five `It.IsAny` lines to express "any query at all". The strongest available assertion about
the query that was actually built is:

```csharp
client.LastQueryFilter.ShouldNotBeNull();
```

That this matters is demonstrable, not theoretical. Bastion's BC field-name strings have
already drifted in casing across adapters:

```
Filter.Equals("no",  ...)   and   Filter.Equals("No",  ...)
Filter.Equals("positive", ...)   and   Filter.Equals("Positive", ...)
```

No test in the consumer could have caught that, because no test can see the emitted URL.

The package already contains the necessary machinery — `test/…/Utils/FakeHttpHandler.cs` —
it is simply not shipped.

### Proposal

Publish a companion package containing:

1. **`FakeBusinessCentralHandler`** — the existing `FakeHttpHandler`, promoted to public API,
   so consumers can build a *real* `BusinessCentralClient` over a scripted transport. This is
   the highest-fidelity option: it exercises URL building, paging, retry and deserialization.
2. **`InMemoryBusinessCentralClient`** — seed entities, query them, and record every request.
   Cheaper than (1) for consumers who only want their own mapping logic under test.
3. **Request capture** on both, exposing the generated relative URL:

```csharp
var client = new InMemoryBusinessCentralClient()
    .Seed("items", new Item { No = "X", Description = "Pump" });

var result = await client.QueryAsync<Item>("items", Filter.Equals("no", "X"),
                                           select: ["no", "description"]);

client.Requests.Single().Url
      .ShouldBe("items?$filter=no eq 'X'&$select=no,description");
```

### Acceptance criteria

- [ ] New project `src/Dynamics365.BusinessCentral.Testing`, published as its own NuGet package.
- [ ] Consumers can assert the exact relative URL produced by a query, including `$filter`,
      `$select`, `$expand`, `$top`, `$skip` and `$orderby`.
- [ ] A fake can be constructed with zero arguments and used without implementing any interface
      member.
- [ ] Multi-page responses can be scripted, including server-driven `@odata.nextLink`, so
      consumers can test their own paging assumptions.
- [ ] Failures can be scripted by status code, so consumers can test their `catch` branches
      without constructing exception types by hand.
- [ ] README section: "Testing against Business Central".

> The last criterion is worth calling out. Bastion currently has comments in two test files
> reading *"BusinessCentralServerException-specific 404 / status-code tests are covered via
> integration tests where we can construct the SDK exception through the real client."* Those
> integration tests do not exist. The branches are untested because constructing the exception
> types by hand is impractical.

---

## P3 — Auto-chunked `In` / key-set fetch

**Priority: High · Effort: Medium · Breaking: No**

### Problem

`Filter.In` exists but goes unused, because using it *correctly* requires knowledge the
consumer does not have: Business Central's URL length limit, and how to chunk, parallelise
and merge around it. Consumers therefore fan out one request per key — against an API this
package's own README describes as throttling aggressively.

### Evidence

`Filter.In` appears **zero times** across the entire Bastion codebase. Meanwhile, its
nameplate adapter issues one HTTP request per item number:

```csharp
var distinct = itemNumbers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

var tasks = distinct.Select(n => _client.QueryAsync<Item>(
    Item.ItemPath,
    Filter.Equals(ItemFields.ItemNumber, n),
    select: [ /* six fields */ ],
    cancellationToken: ct));

var results = await Task.WhenAll(tasks);
```

Unbounded parallelism, N round trips, one 429 away from a partial failure — for what is
semantically a single `no in (...)` query.

### Proposal

Put the chunking in the client, where the URL limit and throttling behaviour are already
known:

```csharp
await client.Query<Item>()
            .WhereIn(i => i.No, itemNumbers)   // chunks, bounds concurrency, merges
            .ToAllAsync(ct);
```

with a path-based equivalent for consumers who have not adopted the builder.

Behaviour to specify: chunk by encoded URL length rather than element count; bound concurrency
(default small, e.g. 4); preserve `$select`/`$expand` across chunks; deduplicate merged
results; and surface a partial-failure policy rather than letting one chunk's 429 discard the
whole set.

### Acceptance criteria

- [ ] `WhereIn` on the builder and an `InChunked`-style helper on the path-based API.
- [ ] Chunk boundaries derived from encoded URL length, with the limit configurable.
- [ ] Concurrency bounded and configurable.
- [ ] Test: 500 keys produce > 1 request, all keys appear exactly once in the merged result.
- [ ] Test: an empty key collection issues **zero** requests and returns empty — consistent
      with the existing `Filter.In` empty-collection semantics.
- [ ] README section, cross-linked from `Filter.In`, since discoverability is the actual
      failure here.

---

## P4 — Native OpenTelemetry

**Priority: Medium · Effort: Medium · Breaking: No**

### Problem

`IBusinessCentralObserver` is the only way to get insight into the client, and it requires
consumers to write and register an implementation. Consumers with an existing OTel pipeline
get nothing automatically — the package is an unlabelled HTTP span.

### Evidence

Bastion implements **zero** observers, despite running OpenTelemetry with Seq and Application
Insights sinks, and despite having a module whose whole purpose is querying that telemetry.
`AddHttpClientInstrumentation` yields a span that says an HTTPS request occurred, with no
entity set, no operation, no company, no retry context.

The friction is that writing an observer is work whose payoff is not obvious up front, so it
never reaches the top of a backlog.

### Proposal

Emit `System.Diagnostics.ActivitySource` spans and `System.Diagnostics.Metrics.Meter`
instruments directly. Both are BCL types — this adds **no** package dependency, which keeps
the existing "no logging dependency" design intent intact.

Suggested span attributes: entity set / path, OData operation, company, HTTP method, status
code, attempt number, whether the token was served from cache.

Suggested instruments: request duration histogram; counters for throttled responses, retries,
token refreshes and cache hits.

Keep `IBusinessCentralObserver` for callback-style consumers; make OTel the default so
upgrading is all that is required to benefit.

### Acceptance criteria

- [ ] A documented `ActivitySource` name, stable across versions.
- [ ] A documented `Meter` name and instrument list.
- [ ] Spans correctly parent under an ambient `Activity`.
- [ ] Retries appear as distinct, correlated attempts rather than one opaque span.
- [ ] No new package dependency.
- [ ] README section: "Observability", with the one-liner needed to wire it into OTel.

---

## P5 — Make the HTTP client names public

**Priority: Medium · Effort: Trivial · Breaking: No**

### Problem

```csharp
internal const string TokenHttpClientName  = "Dynamics365.BusinessCentral.Token";
internal const string ClientHttpClientName = "Dynamics365.BusinessCentral.Client";
```

Because these are `internal`, a consumer cannot address the package's HTTP clients by name.
The practical consequence: a consumer with a global `ConfigureHttpClientDefaults` policy
cannot exempt the Business Central clients from it.

### Evidence

Bastion (via .NET Aspire's standard `ServiceDefaults`) applies
`AddStandardResilienceHandler` to **every** `IHttpClientFactory` client, including this
package's two. With the package's own retry also enabled, the two compose multiplicatively —
roughly nine HTTP requests per logical call — and the package's backoff sleeps consume the
outer handler's 3-minute total-request timeout.

The correct resolution is to disable the *outer* handler for the BC clients and keep the
package's retry, which is strictly better: it honours `Retry-After` and refuses to replay a
`POST` on ambiguous transients. That resolution is unavailable, because the client names are
internal.

Bastion therefore had to take the inferior option — `options.Retry.Enabled = false` — keeping
an outer handler that **retries `POST` by default**, and so can duplicate rows in an entity
set with no uniqueness enforcement. The package's own POST-safety logic is bypassed entirely,
because the outer handler retries before the package ever observes a failure.

### Proposal

Promote both constants to public API on a stable type:

```csharp
public static class BusinessCentralHttpClients
{
    public const string Token  = "Dynamics365.BusinessCentral.Token";
    public const string Client = "Dynamics365.BusinessCentral.Client";
}
```

Optionally add a convenience opt-out — e.g. `AddBusinessCentral(..., configureHttpClient:)`
or a documented `RemoveAllResilienceHandlers()` recipe.

### Acceptance criteria

- [ ] Both names exposed as public constants.
- [ ] README section: "Composing with an existing resilience pipeline", covering both the
      "disable ours" and "disable theirs" options and stating why the latter is preferable.
- [ ] MIGRATION.md's retry-composition note links to it.

---

## P6 — Single-entity fetch on the path-based API

**Priority: Medium · Effort: Low · Breaking: No**

### Problem

`FirstOrDefaultAsync` exists on the typed builder, but there is no path-based equivalent.
Consumers who have not adopted the builder — which, for an existing 1.x codebase, is most of
them — write the same three-line dance at every call site.

### Evidence

This shape recurs throughout Bastion (order lookup, status probe, service-item lookup by key):

```csharp
var orders = await _client.QueryAsync<ProductionOrderEntity>(
    path, Filter.Equals("no", productionOrderNumber), select: [...], cancellationToken: ct);

if (orders.Count == 0)
    return new BcOrderStatusResult.NotFound();

var status = orders[0].Status;
```

### Proposal

```csharp
Task<TEntity?> GetAsync<TEntity>(string path, string key, IEnumerable<string>? select = null,
                                 CancellationToken cancellationToken = default);

Task<TEntity?> FirstOrDefaultAsync<TEntity>(string path, ODataFilter? filter = null,
                                            IEnumerable<string>? select = null,
                                            CancellationToken cancellationToken = default);
```

`GetAsync` should return `null` on `404` rather than throwing — "does this entity exist" is a
question, not an error. Add it as a default interface method (see P1) so it is non-breaking.

### Acceptance criteria

- [ ] Both methods on `IBusinessCentralClient`, as default interface methods.
- [ ] `GetAsync` returns `null` on `404`; all other failures still throw.
- [ ] `FirstOrDefaultAsync` sends `$top=1`.
- [ ] Alternate keys (`No='1000'`) work, consistent with the existing key handling.

---

## P7 — Predicate helpers on `BusinessCentralException`

**Priority: Medium · Effort: Trivial · Breaking: No**

### Problem

The exception types are **sealed siblings**, not a hierarchy. This is a defensible design, but
it invites a specific bug that the compiler cannot catch and that MIGRATION.md already warns
about — which is itself evidence that the shape is a trap.

### Evidence

This clause shipped in Bastion's production code and was **unreachable for the entire 1.0
lifetime**:

```csharp
catch (BusinessCentralServerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    // Already gone — idempotent success
    return Result.Success();
}
```

`404` is `BusinessCentralNotFoundException`, a sealed sibling, so the guard could never match.
The intended idempotent-delete behaviour silently never happened; the call returned a failure
instead. It compiles, reads correctly, passes review, and is wrong.

MIGRATION.md documents this well. But documentation is the weakest available fix for a
mistake the type system permits.

### Proposal

Add predicate properties to the base type so consumers stop pattern-matching on subtypes:

```csharp
public bool IsNotFound   => StatusCode == HttpStatusCode.NotFound;
public bool IsThrottled  => StatusCode == HttpStatusCode.TooManyRequests;
public bool IsValidation => StatusCode == HttpStatusCode.BadRequest;
public bool IsAuth       => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
```

Then the safe form is also the obvious one:

```csharp
catch (BusinessCentralException ex) when (ex.IsNotFound) { ... }
```

Consider additionally a Roslyn analyzer flagging `catch (BusinessCentral*Exception) when
(ex.StatusCode == ...)` where the pairing is unsatisfiable. That would have caught the bug
above at compile time.

### Acceptance criteria

- [ ] Predicate properties on `BusinessCentralException`, alongside the existing `IsTransient`.
- [ ] README error table shows the `when (ex.IsNotFound)` form as the recommended pattern.
- [ ] Optional: analyzer for unsatisfiable status-code guards.

---

## P8 — Public field-name resolution + selector-based `$select` on the path API

**Priority: High · Effort: Low · Breaking: No**

### Problem

Consumers on the path-based API hand-maintain constants classes that duplicate wire names the
entity model **already declares**. The package can resolve those names — `PropertyPath` does
exactly this, honouring `[JsonPropertyName]` before the naming policy — but the capability is
`internal`, and the path-based `select:` parameter accepts only `IEnumerable<string>`.

So a consumer who has not adopted the `Query<T>()` builder has no way to express a field name
in terms of the model, and falls back to string constants maintained by hand.

### Evidence

A representative entity from Bastion. The wire names are declared authoritatively via
`[JsonPropertyName]` — and then copied by hand, immediately below, into a parallel constants
class:

```csharp
public sealed class ProductionOrderLine : DynamicsEntity
{
    public const string ProductionOrderLinesPath = "LDATProdOrderLine";

    [JsonPropertyName("prodOrderNo")]        public string ProductionOrderNumber { get; set; } = default!;
    [JsonPropertyName("itemNo")]             public string ItemNumber { get; set; } = default!;
    [JsonPropertyName("lineNo")]             public int    LineNumber { get; set; }
    [JsonPropertyName("ccoItemCategoryCode")] public string ItemCategoryCode { get; set; } = default!;
    [JsonPropertyName("quantity")]           public decimal Quantity { get; set; } = default!;
    [JsonPropertyName("planningLevelCode")]  public int    PlanningLevelCode { get; set; }
}

public static class ProductionOrderLineFields
{
    public const string ProdOrderNo         = "prodOrderNo";
    public const string ItemNumber          = "itemNo";
    public const string LineNumber          = "lineNo";
    public const string ItemCategoryCode    = "ccoItemCategoryCode";
    public const string Quantity            = "quantity";
    public const string PlanningLevelCode   = "planningLevelCode";
}
```

Six properties, six manually duplicated constants — and the duplication has already drifted
in vocabulary: every constant is named after its C# property **except** `ProdOrderNo`, which
is named after the wire field while the property is `ProductionOrderNumber`. A caller reading
`ProductionOrderLineFields.ProdOrderNo` must know it refers to `ProductionOrderNumber`. This
pattern is repeated across roughly a dozen entity types in the consumer.

Note also that `ProductionOrderLinesPath` is a hand-rolled `[BusinessCentralEntity]`.

**Half of this is already solvable today.** Because `PropertyPath` honours
`[JsonPropertyName]`, the typed filter overloads already emit the correct wire name with no
package change:

```csharp
Filter.Equals<ProductionOrderLine>(l => l.ProductionOrderNumber, poNumber)  // → prodOrderNo
```

The constants survive **only** because of `$select`, which on the path-based API has no
selector-based form.

### Proposal

Two small additions that let consumers delete these classes entirely without adopting the
builder wholesale:

**(a) Make field-name resolution public.** Expose a thin facade over `PropertyPath`:

```csharp
public static class BusinessCentralField
{
    public static string Of<TEntity>(Expression<Func<TEntity, object?>> selector);
}
```

This alone lets a consumer replace a constants class with call-site resolution, and is a
one-line change to visibility plus a public wrapper.

**(b) Add selector overloads for `select:` on the path-based API**, mirroring the builder's
`Select(params Expression<Func<TEntity, object?>>[])`:

```csharp
var lines = await client.QueryAsync<ProductionOrderLine>(
    ProductionOrderLine.Path,
    Filter.Equals<ProductionOrderLine>(l => l.ProductionOrderNumber, poNumber),
    select: [l => l.ItemNumber, l => l.Quantity]);
```

Optionally **(c)**: make `EntityPath.For<T>()` public too, so a consumer that annotates its
entities can drop hand-rolled path constants without being forced onto `Query<T>()`.

Together these remove the last reason to hand-maintain wire names, and they are a fraction of
the cost of the source generator originally considered for this slot.

### Acceptance criteria

- [ ] Public API for resolving a property selector to an OData field name, sharing
      `PropertyPath`'s implementation (not a reimplementation).
- [ ] `QueryAsync` / `QueryAllAsync` / `QueryStreamAsync` accept selector-based `select`
      alongside the existing string form.
- [ ] `EntityPath.For<T>()` exposed publicly.
- [ ] Test: a selector-resolved name for a property carrying `[JsonPropertyName]` returns the
      attribute value, not the camelCase policy result.
- [ ] README section aimed at path-based consumers: "Using field selectors without the query
      builder" — this is the discoverability gap, since the typed `Filter` overloads already
      work today and consumers are not finding them.

---

## Appendix: provenance and confidence

**Method.** Migrated Bastion from `1.0.0` to `2.0.0-alpha`: read `CHANGELOG.md`,
`MIGRATION.md`, `README.md` and the package source; mapped every consumer call site; built;
fixed; ran the full test suite (1,159 unit tests, ~460 integration tests, all green).

**Read closely:** `Client/IBusinessCentralClient.cs`, `Client/BusinessCentralClient.cs`,
`ServiceCollectionExtensions.cs`, `Options/BusinessCentralOptions.cs`,
`Errors/BusinessCentralException.cs`, `OData/PropertyPath.cs`, `OData/EntityPath.cs`,
`Options/BusinessCentralJson.cs`, and the public signatures of `OData/Filter.cs` and
`OData/IBusinessCentralQuery.cs`.

**Skimmed only:** `OData/BusinessCentralQuery.cs`, `Client/BusinessCentralUrlBuilder.cs`,
`Client/BusinessCentralTokenProvider.cs`.

P3 touches the skimmed files. If chunked `In` already exists in some form, discount it
accordingly.

**Version note.** Findings are against tag `v2.0.0-alpha` (commit `1d6dc8f`). At the time of
writing, `master`'s `[Unreleased]` section contains `BusinessCentralConnectionException`,
network-failure retry and the `DateOnly`/`TimeOnly` filter-literal fix — none of which are in
the published alpha, and none of which affect the proposals above.

**Not included.** Defects found during migration are absent from this document because there
were none. Every 2.0 behavioural change encountered was correct, deliberate and documented in
MIGRATION.md; the two latent bugs surfaced (the unreachable `catch`, and the truncating
`QueryAllAsync` reconciliation) were consumer-side, and one of them was *fixed* by upgrading.
