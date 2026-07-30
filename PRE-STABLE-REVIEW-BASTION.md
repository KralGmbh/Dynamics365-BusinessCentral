# Pre-stable review: findings from a production consumer

Second round of feedback on **Dynamics365.BusinessCentral**, written against
`v2.0.0-alpha.2` (commit `a2e12d3`) after a production consumer (Bastion) upgraded onto it.

Round one is [CONSUMER-FEEDBACK-BASTION.md](CONSUMER-FEEDBACK-BASTION.md) and covered the
consumer-facing API surface. **This document is different in kind:** it comes from reading
the client internals — token acquisition, retry backoff, URL construction, paging — rather
than from using the public API. The findings are defects and robustness gaps, not ergonomics
requests.

Self-contained: an agent working in this repository needs no access to the consumer
codebase. All evidence is quoted inline with file references.

---

## Status of round one

Shipped in `2.0.0-alpha.2` — no action needed, listed so this round is not confused with it:

| ID | Proposal | Status |
| --- | --- | --- |
| P1 | Default implementations on every `IBusinessCentralClient` member | ✅ |
| P5 | `BusinessCentralHttpClients` public client names | ✅ |
| P6 | `GetAsync` / `FirstOrDefaultAsync` on the path-based API | ✅ |
| P7 | Predicate properties on `BusinessCentralException` | ✅ (plus `IsConnectionFailure`) |
| P8a/c | `BusinessCentralField.Of`, public `EntityPath` | ✅ |
| P2 | Testing package | ❌ Not implemented — see *Release readiness* below |
| P3 | Auto-chunked `In` | ❌ Not implemented |
| P4 | Native OpenTelemetry | ❌ Not implemented |

Verified against the consumer: the upgrade from `2.0.0-alpha` to `2.0.0-alpha.2` required
**zero** source changes and left 1,378 unit tests plus 208 integration tests green. P1 works
as intended — two hand-written fakes shed ~90 lines of stubs and now declare only the members
they exercise.

---

## Summary of this round

| ID | Finding | Severity | Effort |
| --- | --- | --- | --- |
| N1 | Token acquisition has no retry — and the README now steers consumers into removing the only resilience it had | **High** | Low |
| N2 | No jitter in retry backoff → synchronised retry storms under throttling | **High** | Low |
| N3 | `InvalidateAsync` can cascade token refreshes under concurrent `401`s | Medium | Low |
| N4 | No URL-length guard → confusing failures on large `$filter` | Medium | Low |
| N5 | The paging loop is duplicated verbatim in two files | Medium | Medium |
| N6 | `$expand` hand-rolled encoding; `"true"` magic-string filter contract | Low | Low |

N1 and N2 are the ones I would not ship stable without.

---

## N1 — Token acquisition has no retry, and the README now makes that dangerous

**Severity: High · Effort: Low**

### Problem

`BusinessCentralTokenProvider.GetTokenAsync` performs a single, bare send. It does not go
through `SendWithAuthRetryAsync`, so it has no transient handling of any kind — no retry, no
`Retry-After`, no backoff.

Until `2.0.0-alpha.2` this was masked for most consumers: anyone with a global resilience
handler (Aspire's `ConfigureHttpClientDefaults` + `AddStandardResilienceHandler` is the common
case) was getting retries on the token client for free, without knowing it.

The new README section removes exactly that safety net:

```csharp
services.AddHttpClient(BusinessCentralHttpClients.Client).RemoveAllResilienceHandlers();
services.AddHttpClient(BusinessCentralHttpClients.Token).RemoveAllResilienceHandlers();
```

A consumer who follows the documented guidance ends up with **zero** resilience on token
acquisition. A transient `503` from `login.microsoftonline.com` becomes a hard failure.

### Evidence

`Client/BusinessCentralTokenProvider.cs:83` — the only send in the class:

```csharp
using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
res.RequestMessage ??= req;

if (!res.IsSuccessStatusCode)
    throw await BusinessCentralExceptionFactory.CreateAsync(res, cancellationToken).ConfigureAwait(false);
```

No loop, no `BusinessCentralRetryOptions` reference anywhere in the file.

**Blast radius is larger than the failure rate suggests.** The token is cached for roughly an
hour, so failures are rare — but `GetTokenAsync` serialises every concurrent caller behind one
`SemaphoreSlim` (`:52`). When a refresh fails, every in-flight request fails together.

### Proposal

Route token acquisition through the same retry budget as data requests. The
`client_credentials` grant has no side effects, so replay is unconditionally safe — none of
the `POST`-ambiguity reasoning in `IsSafeToReplay` applies.

Then drop the `.Token` line from the README's resilience snippet, or replace it with an
explicit note that the package now handles token resilience itself.

### Acceptance criteria

- [ ] Token acquisition honours `Retry.Enabled`, `MaxAttempts`, `BaseDelay`, `MaxDelay` and
      `Retry-After`.
- [ ] Token retries raise `OnRequestRetrying`, consistent with data requests.
- [ ] Test: a token endpoint returning `503` then `200` yields a token rather than throwing.
- [ ] Test: a token endpoint returning `401`/`400` (bad credentials) does **not** retry — that
      is not transient.
- [ ] README's resilience snippet no longer strips resilience from the token client.

---

## N2 — No jitter in the retry backoff

**Severity: High · Effort: Low**

### Problem

Backoff is purely deterministic. Every client that fails at the same moment retries at the
same moment.

Against an API the README itself describes as throttling aggressively, this is the textbook
setup for a synchronised retry storm. It is worse on the `Retry-After` path than on the
computed path: Business Central hands *every* concurrent caller the same `Retry-After` value,
so they all resume in lockstep and re-throttle each other.

### Evidence

`Client/BusinessCentralClient.cs:864` — no randomisation, and `Retry-After` is honoured
verbatim:

```csharp
private static TimeSpan ComputeDelay(
    BusinessCentralRetryOptions retry, TimeSpan? retryAfter, int attempt)
{
    var max = Floor(retry.MaxDelay);

    if (retry.HonorRetryAfter && retryAfter is { } requested)
        return Clamp(requested, max);

    var milliseconds = Floor(retry.BaseDelay).TotalMilliseconds * Math.Pow(2, attempt - 1);
    …
}
```

A repository-wide search for `Random` / `Jitter` returns nothing.

Consumers that fan out concurrent Business Central calls amplify this directly — the
consumer here issues parallel `QueryAsync` calls via `Task.WhenAll` in more than one adapter.

### Proposal

Apply jitter to **both** branches — the computed backoff and the honoured `Retry-After`.
Decorrelated jitter is the usual choice; at minimum ±20%.

Expose it as `Retry.JitterFactor` (default non-zero) so it can be disabled for deterministic
tests, and seed from `Random.Shared` to stay allocation-free and thread-safe.

### Acceptance criteria

- [ ] Computed backoff and honoured `Retry-After` are both jittered.
- [ ] Jitter never produces a negative delay, and never exceeds `MaxDelay`.
- [ ] Configurable, with a documented default; `0` disables it.
- [ ] Test: N concurrent retries against one `Retry-After` produce a spread of delays, not one
      value.

---

## N3 — `InvalidateAsync` can cascade token refreshes under concurrent `401`s

**Severity: Medium · Effort: Low**

### Problem

`InvalidateAsync` clears the cache unconditionally. Under a burst of `401`s — secret rotation,
token revocation, clock skew — a request that observed the *old* token can clear a *newly
refreshed* one, forcing another refresh, which the next straggler then clears in turn.

N concurrent `401`s can therefore produce substantially more than one token request, which is
the opposite of what the singleton cache exists to achieve.

### Evidence

`Client/BusinessCentralTokenProvider.cs:116` — no comparison against what the caller saw:

```csharp
public async Task InvalidateAsync(CancellationToken cancellationToken)
{
    await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try { _token = null; }
    finally { _tokenLock.Release(); }
}
```

Called from `Client/BusinessCentralClient.cs:702`, once per `401`, with no knowledge of which
token was rejected:

```csharp
await _tokenProvider.InvalidateAsync(cancellationToken).ConfigureAwait(false);
```

### Proposal

Compare-and-swap. The call site already holds the token it used — it set the `Authorization`
header from it — so pass it back:

```csharp
public async Task InvalidateAsync(string staleToken, CancellationToken cancellationToken)
{
    …
    if (_token is not null && _token.Token == staleToken)
        _token = null;
}
```

A straggler holding an already-replaced token then becomes a no-op.

### Acceptance criteria

- [ ] Invalidation only clears the cache when the stale token matches the cached one.
- [ ] Test: 20 concurrent `401`s against one client trigger exactly one token refresh.

---

## N4 — No URL-length guard

**Severity: Medium · Effort: Low**

### Problem

Nothing checks the length of the constructed URL. A large `Filter.In` silently produces a URL
that Business Central or IIS rejects, and the consumer sees a bare `404`/`414` with no
indication that length was the cause.

### Evidence

`Client/BusinessCentralUrlBuilder.BuildQueryUrl` assembles and returns the URL with no size
check. `Filter.In` accepts an unbounded `IEnumerable<object>`.

Evidence that consumers do not anticipate this: in the consumer reviewed here, `Filter.In` is
used **zero** times — its adapters fan out one request per key instead. Nobody who has not
already been burned reaches for the bulk form.

### Proposal

Throw a clear `ArgumentException` naming the length and the likely culprit when the encoded
URL exceeds a configurable threshold (~2000 chars is the safe practical limit).

This is deliberately the cheap 80% of round one's P3 (auto-chunked `In`). Even without
chunking, converting a mystifying `404` into "this `$filter` produced a 4,210-character URL;
Business Central will reject it — chunk the `In` values" is most of the value.

### Acceptance criteria

- [ ] Configurable maximum URL length with a documented default.
- [ ] Exception message states the actual length, the limit, and suggests chunking.
- [ ] Test: an `In` filter with many values throws before the request is sent.

---

## N5 — The paging loop is duplicated verbatim

**Severity: Medium · Effort: Medium**

### Problem

The auto-paging state machine exists twice, in full, including a private `NextTop` helper
defined identically in both places. This is the subtlest logic in the package — server-driven
`nextLink` versus short-page termination, `$top` as a result cap, `$skip` continuation — and
it is maintained in two copies.

### Evidence

`Client/BusinessCentralClient.cs:259–311` and `OData/BusinessCentralQuery.cs:176–234` carry
matching `serverDriven` flags, matching `inPage < requested` termination checks, and two
private `NextTop` methods.

The code already concedes the hazard, at `BusinessCentralClient.cs:196`:

```csharp
// Top is a result cap, exactly as documented on WithTop; PageSize sizes the round
// trips. This mirrors BusinessCentralQuery<T>.StreamAsync — the two implementations
// must stay in agreement.
```

This is not hypothetical: the `2.0.0-alpha.2` fix making `WithTop` a result cap had to be
applied in both copies. A comment is the only thing keeping them synchronised.

### Proposal

Extract one paging iterator parameterised over `IBusinessCentralQueryExecutor` — the interface
already exists and both call sites already have an executor. Both public entry points then
delegate to it.

Worth doing **before** stable: after 2.0.0 these are two public behaviours that can silently
diverge, and divergence between `QueryAllAsync` and `Query<T>().ToAllAsync()` would be
extremely hard for a consumer to diagnose.

### Acceptance criteria

- [ ] One implementation of the paging loop and of `NextTop`.
- [ ] Both entry points delegate to it.
- [ ] Existing paging tests pass unchanged for both entry points.
- [ ] The "must stay in agreement" comment is deleted rather than reworded.

---

## N6 — Two small correctness nits

**Severity: Low · Effort: Low**

### `$expand` is encoded by hand

`Client/BusinessCentralUrlBuilder.cs:119`:

```csharp
query.Add("$expand=" + string.Join(",", options.Expand).Replace(" ", "%20"));
```

Everything else in the builder uses `Uri.EscapeDataString`; this escapes spaces only. A `#`,
`&` or `+` inside an expand clause — e.g. `Expand("lines($filter=code eq 'A&B')")` — breaks the
URL.

Expand syntax needs `(`, `)`, `$`, `,` and `=` preserved, so it cannot be escaped wholesale —
it needs the same selective, character-class approach `EncodeKey` already demonstrates a few
lines below.

### `"true"` is a magic-string contract between `Filter` and the URL builder

`Client/BusinessCentralUrlBuilder.cs:79`:

```csharp
if (!string.IsNullOrWhiteSpace(filter) && filter != "true")
```

"No filter" is encoded as the literal string `"true"`, coupling two components through a magic
value. Any filter whose rendered form is exactly `true` is silently dropped. Representing
absence as `null` would remove the coupling.

### Acceptance criteria

- [ ] `$expand` uses selective encoding preserving OData expand syntax; test with `&` in a
      nested filter.
- [ ] "No filter" is represented structurally rather than by the string `"true"`.

---

## Release readiness for 2.0.0 stable

Recommended gating, in order:

1. **Ship the testing package (round one, P2).** Still the highest-value item, and nothing in
   `alpha.2` changed that. Every fix in this release — `WithTop` semantics, the kindless
   `DateTime` shift, the paging rules — is verified only against this repository's own fake
   handler. **Consumers have no supported way to assert the OData their code generates.**
   Concretely: when the consumer here converged ten drifted field constants, verifying that
   deserialization still bound required writing a test from scratch against
   `BusinessCentralJson.Options` directly, because no package-provided mechanism could do it.

2. **N1 and N2.** Both are robustness-under-load defects, both are small, and N1 is
   actively steered into by current documentation.

3. **Close the `Unverified` list.** It has now been carried across two prereleases:
   `GetCompaniesAsync`'s response shape, whether Business Central honours
   `Prefer: return=representation`, and server-driven `@odata.nextLink` against a real dataset
   — plus a date-filter smoke test for the `DateOnly`/`TimeOnly` fix. A stable release should
   either verify these against a live tenant or state the limitation plainly in the README.

4. **N5**, before the paging behaviour is frozen as public API in two places.

5. **N3, N4, N6** — desirable, not blockers.

**Can slip to 2.1:** round one's P3 (auto-chunked `In`) and P4 (native OpenTelemetry). Both are
purely additive and non-breaking, so neither needs to gate stable. N4 delivers much of P3's
practical value on its own.

---

## Appendix: provenance and confidence

**Method.** Upgraded a production consumer from `2.0.0-alpha` to `2.0.0-alpha.2`, verified
each round-one item in the source rather than from the changelog, then read the client
internals looking for defects. Every claim above was checked against the code at commit
`a2e12d3`; the quoted line numbers are from that commit.

**Read in full:** `Client/BusinessCentralClient.cs`, `Client/BusinessCentralTokenProvider.cs`,
`Client/BusinessCentralUrlBuilder.cs`, `Client/IBusinessCentralClient.cs`,
`Errors/BusinessCentralException.cs`, `Options/*`, `OData/PropertyPath.cs`,
`OData/EntityPath.cs`, `ServiceCollectionExtensions.cs`, `README.md`, `MIGRATION.md`,
`CHANGELOG.md`.

**Not read end-to-end:** `OData/BusinessCentralQuery.cs` (grepped for the paging loop only),
`OData/Filter.cs` (public signatures only). N5 rests on a structural grep of
`BusinessCentralQuery.cs` rather than a full read — if the duplication is deliberate for a
reason not visible in that region, discount it accordingly.

**Not claimed.** No live Business Central tenant was involved. Nothing here is a report of
observed production failure; N1–N3 are latent failure modes identified by reading, and the
severity ratings reflect blast radius rather than observed frequency.
