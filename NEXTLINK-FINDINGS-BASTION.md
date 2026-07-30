# nextLink and paging findings

Fourth round of feedback on **Dynamics365.BusinessCentral**, narrowed to one subject:
server-driven paging and `@odata.nextLink`.

This closes the item [LIVE-TENANT-FINDINGS-BASTION.md](LIVE-TENANT-FINDINGS-BASTION.md) left
under *Still open* — "an unbounded query over 118,133 rows did not produce the expected paging
behaviour … the least-understood behaviour in the package". It is now understood, and the
round-three read (that these published-page endpoints may not support server-driven paging at
all) was **wrong**: they do.

Measured against the same live production tenant on 2026-07-30, against `LDATItems`
(118,133 rows), plus the Microsoft platform documentation that explains the measurements.

Self-contained: no access to the consumer codebase required. Line references point at
`Dynamics365.BusinessCentral` at `2.0.0-alpha.4`.

---

## Summary

| ID | Finding | Severity | Affects |
| --- | --- | --- | --- |
| N1 | The page-size threshold is a **deployment setting**, not a protocol constant — 20,000 online, configurable on-premises | Medium | docs, any hardcoded page bound |
| N2 | BC emits `@odata.nextLink` only when `$top` **exceeds** that threshold, so at default settings the package's nextLink branch is unreachable and every unbounded query pages by `$skip` | Medium | `QueryPager` tiers 1 and 3 |
| N3 | The package never sends `Prefer: odata.maxpagesize` — the supported, tenant-independent way to request server-driven paging. Page size should be deferred to the server and overridable in setup, not a hardcoded default | **High** | all read paths |
| N4 | If `PageSize` is set above the threshold, `QueryPager`'s "server-driven ⇒ exhausted" rule may terminate early and **silently truncate** | **High, unverified** | `QueryPager.cs:79-81` |

N3 is the actionable one: adopting it makes N1, N2 and N4 stop mattering.

---

## What was measured

A `$top` ladder against `LDATItems` (`$select=no`), same tenant, same token:

| Request | Result |
| --- | --- |
| `$top=5000` | served in full, **no** nextLink |
| `$top=10000` | served in full, **no** nextLink |
| `$top=20000` | served in full, **no** nextLink |
| `$top=25000` | first page, **plus** a nextLink |

The nextLink returned for the last one:

```
https://api.businesscentral.dynamics.com/v2.0/{tenant}/Production/ODataV4
  /Company('KRAL%20AG')/LDATItems?$select=no&$top=5000&aid=FIN&$skiptoken=87432712-5d44-f111-90ed-6045bd9b71d5
```

Three things to read off it:

1. **`$top=5000` is the unserved remainder**, `25000 − 20000`, not the server's page size. The
   threshold sits at 20,000 — consistent with every row of the ladder.
2. The continuation is a **`$skiptoken`**, an opaque cursor, not a `$skip` offset.
3. BC adds `aid=FIN` itself, and `$select` is preserved.

The first page's rows were not counted directly; that it contained 20,000 is inferred from the
remainder arithmetic. Anyone reproducing this should count them.

---

## N1 — The threshold is configuration, not a constant

**Severity: Medium · Documented + measured**

Business Central's paging bound is the **Max Page Size** server setting, called
`ODataServicesMaxPageSize` in `CustomSettings.config`:

> Business Central on-premises and online are set up to use a maximum of **20,000** entities per
> page by default. With Business Central on-premises, page size is determined by a configuration
> setting on the Business Central Server, which you can change. With Business Central online, the
> page size is configured on the service and can't be changed.
> — [Server-Driven Paging in OData Web Services](https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/webservices/server-driven-paging-in-odata-web-services)

So the 20,000 measured above is *this tenant's* value, and it is fixed only because this tenant
is SaaS. An on-premises consumer whose administrator set Max Page Size to 5,000 — a reasonable
choice, since the docs warn that a large value "can overload the memory on Business Central
Server" — gets different behaviour from byte-identical client code.

Page objects do not enter into this. The only page-level interaction the docs describe is
`TopNumberOfRows` on *query* objects, which does not apply to published page web services.

**Consequence:** the package must not encode 20,000, nor derive the threshold empirically. Any
paging strategy whose correctness depends on knowing it is not portable across deployments.

---

## N2 — At default settings, the nextLink branch is unreachable

**Severity: Medium**

`QueryPager` has a three-tier termination scheme (`OData/QueryPager.cs:11-21`): follow
`@odata.nextLink` when present; once server-driven, a missing nextLink means exhausted;
otherwise stop on the first short page.

Every read path sends an explicit `$top` — `pageSize`, defaulting to 1,000
(`QueryPager.cs:25`). Per the ladder, any `$top` at or under the threshold is served whole
with no nextLink. Therefore:

- **Tier 1 never fires** at default settings. The branch at `QueryPager.cs:70` is only
  reachable if a caller sets `PageSize` above the server's threshold.
- **Tier 3 is what actually runs** for every unbounded query: full page ⇒ `skip += inPage` ⇒
  request again.

That is why round three saw no paging behaviour on a 118,133-row sweep. Nothing was broken;
the observation was taken at a page size that cannot produce a nextLink.

Two costs follow. A full `LDATItems` sweep at `pageSize` 1,000 is **119 round trips** against
an API that throttles hard. And `$skip` paging over a set with no explicit `$orderby` is only
as stable as BC's default row order — the classic offset-paging hazard, where a concurrent
insert or delete shifts the window and rows are duplicated or skipped between requests. A
`$skiptoken` cursor has neither problem.

---

## N3 — `Prefer: odata.maxpagesize` is never sent

**Severity: High · This is the fix**

Business Central supports a per-request override of server-driven paging:

> To set paging on a request, use the `odata.maxpagesize` preference in the `Prefer` header of
> the HTTP request: `Prefer: odata.maxpagesize=300`
>
> `odata.maxpagesize` can't be greater than the **ODataServicesMaxPageSize** server setting for
> on-premises and 20000 for online.
> — [Server-Driven Paging in OData Web Services](https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/webservices/server-driven-paging-in-odata-web-services)

The package sends `Prefer: return=representation` on writes
(`Client/HttpRequestExtensions.cs:31`, applied at `Client/BusinessCentralClient.cs:345` and
five sibling call sites) and **no `Prefer` header on any read**. Server-driven paging is
therefore never requested — it is only stumbled into, when a caller's `$top` happens to
overshoot a threshold the client cannot see.

### Proposal

Stop using `$top` to size round trips on the streaming paths — it stays what `WithTop`
documents it to be, a result cap — and let page size be **negotiated with the server** instead
of decided by the client.

The key point is what the *default* should be. `QueryPager.DefaultPageSize = 1000` is a number
the package invented; it corresponds to nothing about any deployment, and per N1 the package
cannot know the right value anyway. **The correct default is to send no `$top` and no
preference at all.** The server then applies its own Max Page Size — the value its
administrator actually configured — and drives the paging by nextLink. The deployment's
configuration becomes the default, and the package ships no page-size constant.

Consumers who need something other than the server's value get it through setup, which is also
the reason Microsoft documents the override — "useful when pages are slow and you're
experiencing timeouts or out of memory exceptions". Three levels, most specific winning:

1. `BusinessCentralOptions.MaxPageSize` (nullable, **default null**) — set at registration,
   sent as `Prefer: odata.maxpagesize={value}` on every streaming read. Null means "defer to
   the server".
2. `QueryOptions.WithPageSize(n)` — per-query override of the above, for the one sweep that
   needs different pacing. This already exists; it would now emit the preference rather than
   `$top`.
3. No fallback constant. Where `DefaultPageSize` is used today, nothing is sent.

Note the direction of control: the preference can only ask the server for a page **no larger**
than its own setting — it is clamped, not honoured blindly — so an over-large value cannot
break a request, and a consumer cannot use this to raise a deployment's ceiling.

Every consequence above then dissolves at once:

- BC emits a nextLink on **every** page on **every** deployment, so tier 1 becomes the live
  path and tier 3 becomes the fallback it reads like.
- The package never needs to know the threshold, because it never states one (N1 stops mattering).
- Continuation is by `$skiptoken`, so the offset-stability hazard in N2 disappears.
- The N4 hypothesis below becomes moot: the chain is no longer bounded by a caller's `$top` budget.
- Round trips drop to whatever the server's own page size allows — for a SaaS tenant at 20,000,
  a full `LDATItems` sweep goes from 119 requests to 6.

Two requests validate the design before any code is written, both on `?$select=no` with **no
`$top`**: one with no `Prefer` header (expect the server's own page size and a nextLink), one
with `Prefer: odata.maxpagesize=1000` (expect 1,000 rows and a nextLink).

### Acceptance criteria

- [ ] `BusinessCentralOptions.MaxPageSize` exists, is nullable, and defaults to null.
- [ ] Streaming reads send `Prefer: odata.maxpagesize={value}` only when a page size was
      configured — never a package-chosen default.
- [ ] `QueryPager.DefaultPageSize` is gone; no page-size constant remains in the package.
- [ ] `pageSize` no longer determines `$top` on streaming reads; `WithTop` remains a pure result cap.
- [ ] A test asserts no `Prefer: odata.maxpagesize` header when nothing is configured.
- [ ] A test asserts the header carries the option value, and that `WithPageSize` overrides it.
- [ ] A test asserts a caller-set `WithTop` still caps emitted rows when the server pages independently.
- [ ] README documents that page size defaults to the server's configuration, that the option
      requests a smaller page, and that the server clamps it.

---

## N4 — Possible silent truncation above the threshold

**Severity: High if confirmed · UNVERIFIED — do not act on this without the probe below**

`QueryPager` treats server-driven mode as authoritative about the end of the collection:

```csharp
// The server was paging and stopped offering a nextLink: nothing left.
if (serverDriven)
    yield break;
```
`OData/QueryPager.cs:79-81`

Against BC that assumption may be false. The evidence says the nextLink is a **remainder
continuation of the caller's `$top`** — `25000 − 20000 = 5000` — not an open-ended cursor over
the collection. If BC stops offering nextLinks once the requested `$top` is satisfied rather
than when rows run out, then `PageSize = 25000` with no `Top` over `LDATItems` yields
20,000 + 5,000 rows and returns **25,000 of 118,133 as a successful, complete result**.

That is the same silent-truncation failure class as the 1.x behaviour this machinery was built
to fix, re-entering through a different door — and only on the code path a consumer reaches by
tuning for throughput.

**Decisive probe:** follow the captured nextLink once.

- Page 2 returns 5,000 rows and **no** further nextLink ⇒ confirmed. `PageSize` needs a
  documented ceiling and validation, and tier 2's comment is wrong against BC.
- Page 2 returns another nextLink ⇒ BC treats `$skiptoken` as an open cursor, `QueryPager` is
  correct as written, and this finding can be struck.

Adopting N3 removes the exposure either way, since page size would then be negotiated rather
than smuggled through `$top`.

### Acceptance criteria

- [ ] Run the probe and record the outcome here.
- [ ] If confirmed: `PageSize` validated against a documented maximum, or the streaming paths
      switched to `Prefer` (N3) so the case cannot arise.

---

## What is already correct

Worth stating, since the rest of this document is problems.

`FetchNextPageAsync` (`Client/BusinessCentralClient.cs:300`) sends the nextLink verbatim as an
absolute URL through the normal authenticated send path. That is the right thing to do with a
continuation link, and it matters here: the real URL arrives pre-encoded
(`Company('KRAL%20AG')`) and carries an opaque `$skiptoken` plus a server-added `aid=FIN`. Any
attempt to parse, rebuild or re-encode it would corrupt the cursor. It survives intact and the
bearer token is attached.

The `Top`-as-cap arithmetic in `QueryPager.NextTop` is also correct across a server-paged
stream: a caller-set `WithTop` is honoured mid-page and never overshot by a request.

---

## Still open

| Item | Status |
| --- | --- |
| N4 truncation hypothesis | Needs the one-request probe above. |
| Behaviour with **no** `$top` at all | Not yet measured cleanly. This is the shape N3 would produce and the one round three attempted. |
| `Prefer: return=representation` | Still deliberately unverified — confirming it requires a real write to a production tenant. |

---

## Appendix: provenance

**Method.** A production consumer (Bastion, event-sourced modular monolith, live since
2026-05-03) running `2.0.0-alpha.3`, pointed at its live BC production environment. The `$top`
ladder was executed in Postman against `LDATItems` with `$select=no`. N1 and N3 come from the
Microsoft platform documentation, quoted above; N2 and N4 come from reading `QueryPager` and
`BusinessCentralClient` against those measurements.

**Confidence.** The ladder is direct observation, one entity set, one tenant. N1 and N3 are
documented platform behaviour, not inference. N2 follows from the ladder plus the package's
own default. **N4 is a hypothesis** — it is consistent with every measurement taken, and no
measurement yet distinguishes it from the alternative.

**Not claimed.** No defect is alleged in the nextLink-following code itself; it handles the
real wire shape correctly. The findings are about which paging mechanism the package ends up
using by default, and whether it can tell when a collection has actually ended.

---

## Validation addendum (package side, 2026-07-30)

The two design-validation requests proposed under N3 were executed against the same tenant
before implementation:

| Probe | Request | Result |
| --- | --- | --- |
| Server default | `LDATItems?$select=no`, no `$top`, no `Prefer` | ~20,000 rows **plus** a nextLink — and the continuation carries **no `$top`**: an open-ended `$skiptoken` cursor. |
| Preference | same, with `Prefer: odata.maxpagesize=1000` | exactly 1,000 rows plus a nextLink. |

N3 is therefore implemented as proposed: no default page size, `Prefer: odata.maxpagesize`
when configured (registration-level `BusinessCentralOptions.MaxPageSize`, per-query
`WithPageSize`), `$top` only for caller-set result caps, continuation by nextLink only —
the `$skip` loop is gone.

**N4 remains unprobed** (following the `$top=25000` remainder link once). Under the
implemented design it is unreachable — no code path smuggles a page size through `$top`
anymore — but the probe is still worth one request to settle the record.
