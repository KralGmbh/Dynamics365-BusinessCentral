# Live-tenant facts

Everything else in this repository is tested through `FakeHttpHandler`, a scripted transport. That
proves what the client *sends*. It cannot prove that Business Central accepts it, and it cannot
notice when Business Central changes.

This project is the other half. Each fact here measures something about a real tenant that the
package's behaviour or documentation depends on, so that a claim which turns out to be wrong fails
a test instead of surviving in prose.

## Running it

```bash
dotnet test test/Dynamics365.BusinessCentral.LiveTenant.Tests/Dynamics365.BusinessCentral.LiveTenant.Tests.csproj
```

Without credentials every fact **skips** rather than fails — a contributor with no tenant should
not see permanent red. Configure it with `BC_TENANT_ID` / `BC_CLIENT_ID` / `BC_CLIENT_SECRET`, or a
gitignored `.env/dev-tenant.md` at the repository root.

The CI job (`.github/workflows/live-tenant.yml`) asserts the secret is present before running,
because "all skipped" and "all passed" are the same colour. It never runs on `pull_request`:
secrets are unavailable to fork PRs, and `pull_request_target` is the classic way to leak them.

## Safety

Two independent controls, in order of strength:

1. **The credential.** The app registration is granted on the sandbox environment only and
   deliberately never added to Production, so a token issued for it is rejected at Production's
   door. This holds regardless of what the code does.
2. **`LiveTenant.GuardEnvironment`.** Fails before a request leaves the process if the resolved
   base URL does not name the sandbox. It requires the marker to be *present* rather than testing
   for the absence of "Production", so a renamed or added environment fails closed.

## What is measured

| Fact | What it pins |
| --- | --- |
| `TenantShapeTests` | Row counts, date columns, and that these pages honour `$count` — the last of which decides whether `CountAsync` costs one round trip or a full walk |
| `PagingTests` | A server-issued `@odata.nextLink` is followed, and the full set arrives once each — with and without a client page preference |
| `DateFilterTests` | A kindless `DateTime` filters as UTC, and how many rows the 1.0 machine-local reading would have moved |
| `ProjectionTests` | Derived `$select` resolves against `$metadata`; `$select` is case-insensitive and answered in the page's own casing |
| `SchemaVersionTests` | Native `in` is `501` without `$schemaversion=2.1` and matches the or-chain with it |
| `QueryStringCeilingTests` | Over-length is answered `414`, and a query string inside the warning band is still served |

Numbers the tenant owns — the server's Max Page Size, row counts, the exact size of the date shift
— are **reported, not asserted**. Pinning them would turn an unrelated administrative change into
a package regression. What is asserted is the behaviour the package is responsible for.

## Deliberately not covered

**Writes.** This suite is read-only. The write path — `Prefer: return=representation`, the `204`
branch, `SystemId` read-back — is exercised against the same tenant by the consuming application's
own live suite, and adding a second writer to a shared sandbox buys less than it risks. If this
project ever grows a write fact, it needs the marker-prefix and sweep-before-run discipline that
suite already uses; do not add one without it.
