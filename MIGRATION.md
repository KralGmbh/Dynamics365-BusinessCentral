# Migrating from 1.0 to 2.0

2.0 fixes several defects and reworks the consumer-facing API. Most existing code keeps
compiling — the changes that need your attention are mostly **silent behavioural** ones,
so read that section even if the build is green.

---

## 1. Changes that can break the build

### `IBusinessCentralClient` gained members

New members: `Company`, `ForCompany`, `Query<T>()` (two overloads), `QueryStreamAsync<T>`,
`GetCompaniesAsync`, and two-generic `PostAsync`/`PatchAsync`/`PutAsync`.

Mocking libraries (Moq, NSubstitute, FakeItEasy) absorb this automatically. **A
hand-written test fake implementing the interface will not compile** until the new members
are added.

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
> can never match. Catch `BusinessCentralNotFoundException`, or the base type.

### Transient failures are retried automatically

`429`, `408`, `502`, `503` and `504` are now retried (3 attempts by default), honouring
`Retry-After`. Calls therefore take longer before surfacing a failure, and fewer transient
errors reach your code.

**Writes are not blindly replayed.** A `429` is rejected before processing so replay is
always safe; the others are ambiguous, so a `POST` is *not* retried on them:

| Method | `429` | `408`/`502`/`503`/`504` |
| --- | --- | --- |
| `GET`, `PUT`, `DELETE` | retried | retried |
| `PATCH` | retried | retried — this client sends absolute values, so replay converges |
| `POST` | retried | **not** retried |

If you already have an outer retry policy (Polly, Wolverine, a message broker), the two now
compose multiplicatively. Consider lowering `Retry.MaxAttempts`, or disabling it:

```csharp
options.Retry.Enabled = false;   // 1.0 behaviour
```

### `QueryAllAsync` may return more rows than before

It now follows `@odata.nextLink`. Where Business Central applied a server-side page cap
below the requested `$top`, 1.0 stopped early and silently truncated. Reconciliation logic
that assumed the old result size should be re-checked.

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

In `QueryAllAsync`, `WithTop(n)` meant *page size*, not a result limit. `WithPageSize(n)`
now says that explicitly. `WithTop` still works there (`PageSize ?? Top ?? 1000`), but
prefer the clearer name.

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

- [ ] Update hand-written `IBusinessCentralClient` fakes, if any
- [ ] Audit `catch (BusinessCentralServerException)` — it does not see `400`/`401`/`403`/`404`/`429`
- [ ] Decide on retry: keep it, tune `MaxAttempts`, or `Retry.Enabled = false`
- [ ] Re-check any logic that assumed a `204` write failed
- [ ] Re-check result-size assumptions around `QueryAllAsync`
- [ ] Replace `dynamic` writes with the two-generic overloads
- [ ] Optionally simplify configuration and drop hand-built `BaseUrl`
