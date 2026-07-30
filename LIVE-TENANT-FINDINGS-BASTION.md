# Live-tenant findings

Third round of feedback on **Dynamics365.BusinessCentral**, from validating `2.0.0-alpha.3`
against a **live production Business Central tenant** (Dynamics 365 BC SaaS, OData v4).

The earlier rounds were desk work: [CONSUMER-FEEDBACK-BASTION.md](CONSUMER-FEEDBACK-BASTION.md)
reviewed the consumer-facing API, [PRE-STABLE-REVIEW-BASTION.md](PRE-STABLE-REVIEW-BASTION.md)
reviewed the internals. **This round is empirical** — everything below was observed against a
real tenant, and one finding contradicts a proposal I made in round one.

Self-contained: no access to the consumer codebase required.

---

## Summary

| ID | Finding | Severity | Affects |
| --- | --- | --- | --- |
| L1 | **Business Central does not support the OData `in` operator** — `Filter.In` cannot work against BC | **Critical** | `Filter.In`, round-one P3 |
| L2 | URL-length guard (N4) is more important than previously argued | Medium | round-two N4 |
| L3 | Exception diagnostics performed excellently in the field | — | positive |
| L4 | `eq null` matches **empty strings** on BC text fields | Medium | `Filter.IsNull` / `IsNotNull` docs |
| L5 | BC date fields are **always** date-only, never timestamps | Medium | README modelling guidance |
| L6 | `GetCompaniesAsync` verified — model is correct, all four properties populate | — | closes an Unverified item |

L1 invalidates part of round one's P3 and should block 2.0.0 stable, because `Filter.In` is
currently documented as a working feature.

## Operator support against Business Central — measured

Every row below was executed against the live tenant on `LDATItems` unless noted.

| Feature | Package API | Result |
| --- | --- | --- |
| `eq` | `Filter.Equals` | ✅ works |
| `or` chains | `.Or(...)` | ✅ works — the viable bulk-key idiom |
| `startswith` | `Filter.StartsWith` | ✅ works |
| `contains` | `Filter.Contains` | ✅ works |
| `ge` on a date | `Filter.GreaterOrEqual` | ✅ works (`LDATSalesHeader`) |
| `eq null` | `Filter.IsNull` | ✅ works — **but see L4** |
| `$count` | `QueryOptions.IncludeCount`, `ToPageAsync`, `CountAsync` | ✅ works (`@odata.count: 118133`) |
| `$orderby` + `$skip` | `OrderBy*`, `WithSkip` | ✅ works |
| `$select` | `select:` / `.Select(...)` | ✅ works |
| **`in`** | **`Filter.In`** | ❌ **`BadRequest_MethodNotImplemented`** |
| Server-driven paging | `QueryAllAsync` nextLink following | ⚠️ inconclusive — see *Still open* |

`NotEquals`, `GreaterThan`, `LessThan`, `LessOrEqual` and `EndsWith` were not measured, but
the operators that were all behave as standard OData v4, so `in` looks like the outlier
rather than the pattern.

---

## L1 — Business Central rejects the `in` operator

**Severity: Critical · Verified against a live tenant 2026-07-30**

### Observation

```
GET .../ODataV4/Company('KRAL AG')/LDATItems
      ?$filter=no in ('EBH100','EBT200')
      &$select=no,description,description2,ccsDMDescription3,ccsDMDescription4,ccsDMDescription5
```

```json
{
  "error": {
    "code": "BadRequest_MethodNotImplemented",
    "message": "The OData filter expression is not supported.  CorrelationId: 4df6da19-..."
  }
}
```

The equivalent OR-chain succeeds on the same entity set, same `$select`, same token:

```
?$filter=(no eq 'EBH100') or (no eq 'EBT200')      →  200 OK, rows returned
```

So this is not authentication, not the entity set, and not the projection — Business Central
rejects the `in` operator itself. `BadRequest_MethodNotImplemented` reads as a platform-level
rejection rather than an entity-specific one, and Microsoft's OData documentation for the
Dynamics 365 platform lists `in` as unsupported.

### Why this matters more than a missing operator

`Filter.In` is presented as a first-class, safe API. Its XML docs go out of their way to
describe the empty-collection behaviour:

> An empty `values` produces a filter that matches nothing (`false`) rather than the invalid
> OData expression `field in ()`. This makes `Filter.In(field, ids)` safe when `ids` turns out
> to be empty.

Nothing signals that the operator does not work against the only backend this package targets.

**The failure mode is quiet.** In the consumer, the calling adapter catches broadly and
degrades to a best-effort empty result, so switching a nameplate lookup from per-item `eq` to
`Filter.In` produced no exception, no dead letter, and no log — just silently missing
descriptions in the UI. A consumer following the README could ship that.

It also survived a full unit-test suite. Tests written with
`Dynamics365.BusinessCentral.Testing` asserted the generated OData exactly:

```csharp
request.DecodedPathAndQuery.ShouldContain("$filter=no in ('EBH100','EBT200')");
```

That test passed, and was worthless — the fake answers whatever it is scripted to answer. It
validates the consumer's half of the contract and can say nothing about BC's half. Worth
noting when weighing what the testing package can and cannot prove.

### Impact on round-one P3

Round one proposed **auto-chunked `In`** (`WhereIn` with URL-length chunking). That proposal
is built on an operator BC will not execute. It needs re-basing onto OR-chained `eq`:

```csharp
// what the consumer now does by hand, and what P3 should generate
chunk.Select(n => Filter.Equals(field, n))
     .Aggregate((left, right) => left.Or(right));
```

This is still the right feature — it collapsed an N-request fan-out into one round trip
against an API that throttles hard — but the generated expression must be an OR-chain.

### Proposal

1. **Document the limitation on `Filter.In` itself**, in XML docs and the README's filter
   table: BC rejects `in`; use OR-chained `eq`. Right now the operator table lists
   `Filter.In` → `field in (...)` with no caveat.
2. **Re-base P3 onto OR-chains.** `WhereIn` should emit OR-chained `eq`, chunked. OR-chains
   are roughly twice the encoded length of `in (...)`, so the chunk size must be smaller — the
   consumer settled on 25 short item numbers to stay near 1 kB of encoded `$filter`.
3. **Consider whether `Filter.In` should exist at all** in a BC-specific client, or should be
   an OR-chain builder behind the same name. Emitting an expression the target backend always
   rejects is a trap, and a name that silently does the working thing is defensible here
   precisely because this package is not a general-purpose OData client.

### Acceptance criteria

- [ ] `Filter.In` XML docs and the README filter table state that BC rejects `in`.
- [ ] `WhereIn` / chunked bulk lookup emits OR-chained `eq`, not `in (...)`.
- [ ] Chunk sizing accounts for OR-chain verbosity; a test asserts the encoded URL stays
      within the configured limit.
- [ ] A test asserts the emitted filter contains no ` in (` for the bulk-lookup path.

---

## L2 — The URL-length guard matters more than round two argued

**Severity: Medium**

Round two's N4 proposed throwing a clear error when a generated URL exceeds a safe length,
and rated it Medium as "the cheap 80% of P3". L1 raises its value:

The only BC-viable way to express a bulk key lookup is an OR-chain, which is far wordier than
`in (...)`. Where `'EBH100',` costs about 10 encoded characters per key, `(no eq 'EBH100') or `
costs roughly 40 once quotes, spaces and parentheses are percent-encoded. Consumers doing bulk
lookups will therefore approach BC's URL limit **four times faster** than the `in`-based sizing
in round one's P3 assumed.

Without a guard, exceeding it produces an opaque `400`/`404` from BC rather than anything
pointing at filter length.

### Acceptance criteria

- [ ] As N4, plus: the exception message mentions OR-chain verbosity as a likely cause when the
      filter contains repeated ` or `.

---

## L3 — What worked well in the field

Recording this because it materially shortened debugging, and it is a direct result of the 2.0
error rework.

A consumer-side model bug (a `DateTimeOffset` property bound to a BC `Edm.Date` field that
returns `"0001-01-01"` when unset) surfaced as:

```
BusinessCentralServerException: Failed to deserialize Business Central response. (GET → HTTP 200 OK)
 ---> JsonException: The JSON value could not be converted to System.DateTimeOffset.
      Path: $.value[0].promisedDeliveryDate
--- Business Central details ---
Status: 200 OK
Method: GET
URL:    .../LDATSalesHeader?$filter=No eq %27BKP141911%27&$select=...
Response: {"@odata.context":"...","value":[{...,"promisedDeliveryDate":"0001-01-01",...}]}
```

`RequestUrl` plus the full `ResponseBody` on the exception meant the offending field, its
actual value, and the exact request were all visible from a single log entry — diagnosed and
fixed without a repro. In 1.x this would have been a bare `JsonException`.

Two design decisions paid off concretely here: populating `ResponseBody` (a round-one fix), and
`ToString()` rendering the full diagnostic picture while `Message` stays one line.

---

## L4 — `eq null` matches empty strings on BC text fields

**Severity: Medium**

`Filter.IsNull` works, but not with the semantics a .NET developer will assume.

```
?$filter=ccsDMDescription4 eq null&$top=2&$select=no    →  200, returns item "40701"
```

Fetching that same item in full shows the field is **not** null:

```json
{ "no": "40701", "ccsDMDescription4": "" }
```

Business Central's AL text fields cannot be null — they are empty strings — and its OData
layer maps `eq null` onto "is blank". So `Filter.IsNull` on a text field means *"null or empty"*,
and `Filter.IsNotNull` correspondingly **excludes** empty strings.

That is reasonable BC behaviour, but the XML docs currently describe these as
`field eq null` / `field ne null` with no semantic note, and a consumer filtering for
"description not set" versus "description not empty" will get results that differ from the
equivalent LINQ predicate.

### Acceptance criteria

- [ ] `Filter.IsNull` / `IsNotNull` XML docs state that BC treats blank text as null.
- [ ] README's filter table carries the same note.

---

## L5 — BC date fields are always date-only, never timestamps

**Severity: Medium (consumer guidance)**

```
?$filter=promisedDeliveryDate ge 2020-01-01&$top=3&$select=no,promisedDeliveryDate
```

```json
{ "no": "BKP086052", "promisedDeliveryDate": "2026-10-28" }
{ "no": "BKP105729", "promisedDeliveryDate": "2028-12-07" }
```

Two things worth recording:

**The outbound side works.** The `2020-01-01` literal was accepted, confirming alpha.2's
`DateOnly`/`TimeOnly` filter fix against a real `Edm.Date` field.

**The inbound side is a trap for consumers.** These are populated, real dates — and they come
back date-only, exactly like the `"0001-01-01"` unset sentinel. `System.Text.Json` cannot read
either form into a `DateTimeOffset`, so **any** consumer property typed `DateTimeOffset` and
bound to a BC date field fails deserialization on every row, not merely on unset values.

In the consumer this presented as a hard ingestion failure (`The JSON value could not be
converted to System.DateTimeOffset. Path: $.value[0].promisedDeliveryDate`) and was fixed with
a property-level converter that accepts date-only and normalises to midnight **UTC** — the
naive fast path, `Utf8JsonReader.TryGetDateTimeOffset`, silently applies the *machine's local
offset* to a date-only string, which is the same timezone-dependence alpha.2 fixed on the
outbound side.

### Proposal

README guidance in the querying/modelling section: map BC date fields to `DateOnly`, or supply
a converter — never a bare `DateTimeOffset`. Optionally ship the converter, since every BC
consumer with a date field needs the identical thing.

### Acceptance criteria

- [ ] README documents that BC returns `Edm.Date` as date-only and shows the correct mapping.
- [ ] Optional: a supported `DateOnly`-tolerant converter in the package.

---

## L6 — `GetCompaniesAsync` verified (Unverified item closed)

```
GET .../ODataV4/Company
```

```json
{ "Name": "KRAL AG", "Display_Name": "KRAL GmbH",
  "Id": "b3e1bee3-b045-f111-a820-7ced8d0c999e", "Evaluation_Company": false }
```

`BusinessCentralCompany` binds all four properties correctly — `Name`, `Display_Name`, `Id`
and `Evaluation_Company` match the live payload exactly. The changelog's caveat that
`DisplayName`, `Id` and `IsEvaluationCompany` are "best-effort and null when the endpoint does
not return them" can be softened: against a BC SaaS tenant this endpoint returns all of them.

No change required. The Unverified entry can be struck.

---

## Still open

| Item | Status |
| --- | --- |
| Server-driven `@odata.nextLink` paging | **Inconclusive.** An unbounded query over 118,133 rows did not produce the expected paging behaviour; the consumer's read is that these published-page web-service endpoints (`LDAT*`) may not do server-driven paging at all, unlike the `/api/v2.0` endpoints. Needs a precise capture of the response before any conclusion. **This is the item that silently truncated in 1.x and it is still the least-understood behaviour in the package.** |
| `Prefer: return=representation` | Deliberately unverified — confirming it requires a real write to a production tenant. Better documented as unverified than tested by creating a live record. |

**Recommendation:** given L1, treat every `Filter.*` operator as unverified until measured. The
table above closes most of them; `NotEquals`, `GreaterThan`, `LessThan`, `LessOrEqual` and
`EndsWith` remain unmeasured and each costs one request.

---

## Appendix: provenance

**Method.** A production consumer (Bastion, event-sourced modular monolith, live since
2026-05-03) upgraded to `2.0.0-alpha.3`, pointed at its live BC production environment with
write ports disabled, and ingested real production orders. L1 was found by changing a real
N+1 lookup to `Filter.In`, observing no failure, and then testing the generated URL directly
in Postman.

**Confidence.** L1 is a direct observation with the error code and correlation ID above,
reproduced against one entity set (`LDATItems`) with a working OR-chain control on the same
set. Not tested across multiple entity sets — but the error code and Microsoft's platform
documentation both indicate a platform-level limitation rather than an entity-specific one.

**Not claimed.** No package defect is alleged in L1 — `Filter.In` emits valid OData v4.01. The
finding is that valid OData v4.01 is not sufficient for Business Central, and the package does
not say so.
