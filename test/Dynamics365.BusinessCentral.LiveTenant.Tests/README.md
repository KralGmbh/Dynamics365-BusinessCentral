# Live-tenant facts

Everything else in this repository is tested through `FakeHttpHandler`, a scripted transport. That
proves what the client *sends*. It cannot prove that Business Central accepts it, and it cannot
notice when Business Central changes.

This project is the other half. Each fact here measures something about a real tenant that the
package's behaviour or documentation depends on, so that a claim which turns out to be wrong fails
a test instead of surviving in prose.

## Running it

```bash
BC_LIVE_TENANT_TESTS=1 \
  dotnet test test/Dynamics365.BusinessCentral.LiveTenant.Tests/Dynamics365.BusinessCentral.LiveTenant.Tests.csproj
```

Two conditions, both required. **Credentials** — `BC_TENANT_ID` / `BC_CLIENT_ID` /
`BC_CLIENT_SECRET`, or a gitignored `.env/dev-tenant.md` at the repository root — and the
**`BC_LIVE_TENANT_TESTS` opt-in**. Missing either, every live fact *skips* rather than fails, so a
contributor with no tenant never sees permanent red.

The opt-in exists because this project is part of the solution, so `dotnet test` over the solution
discovers it. Keeping it in the solution is deliberate — an excluded project stops being
maintained — but without a second signal, an ordinary unit-test run on a machine that happens to
have credentials would quietly become a live-tenant run, and routine local testing would start
depending on external state.

The CI job (`.github/workflows/live-tenant.yml`) asserts all three credentials are present before
running, because "all skipped" and "all passed" are the same colour — and an unset repository
*variable* skips the suite just as effectively as a missing secret.

It runs only on pushes to master and on a weekly schedule. Not on `pull_request`, because secrets
are unavailable to fork PRs and `pull_request_target` is the classic way to leak them; and not on
`workflow_dispatch`, because a dispatch runs the workflow definition from the ref it is given, so
anyone able to push a branch could edit the job on that branch and read the secret out of an
unreviewed run. Restoring manual runs safely means putting the secret in a GitHub Environment with
a deployment-branch policy limited to master.

`GuardEnvironmentTests` needs no tenant and always runs.

## Safety

Two independent controls, in order of strength:

1. **The credential.** The app registration is granted on the sandbox environment only and
   deliberately never added to Production, so a token issued for it is rejected at Production's
   door. This holds regardless of what the code does.
2. **`LiveTenant.GuardEnvironment`.** Fails before a request leaves the process unless the resolved
   base URL is the sandbox, on Business Central's own host, over HTTPS. The host check is not
   decoration: every request here carries a real access token, and the credential grant above
   constrains which environment that token opens, not who receives it — so a base URL pointing
   elsewhere would disclose it whatever the path said. The environment must be *present* rather
   than "Production" absent, so a renamed or added environment fails closed.

## What is measured

| Fact | What it pins |
| --- | --- |
| `TenantShapeTests` | Row counts, date columns, and that these pages honour `$count` — the last of which decides whether `CountAsync` costs one round trip or a full walk |
| `PagingTests` | A server-issued `@odata.nextLink` is followed on the wire, and the full set arrives once each — with and without a client page preference |
| `DateFilterTests` | A kindless `DateTime` filters as UTC, and how many rows the 1.0 machine-local reading would have moved; CI fixes a non-UTC timezone so the comparison discriminates |
| `ProjectionTests` | Derived `$select` resolves against `$metadata`; wrong-case `$select` is applied and answered in the page's own casing |
| `SchemaVersionTests` | Native `in` is `501` without `$schemaversion=2.1` and matches the or-chain with it |
| `QueryStringCeilingTests` | Adjacent query shapes immediately below and above 8,099 are respectively served and answered `414` |

Numbers the tenant owns — the server's Max Page Size, row counts, the exact size of the date shift
— are **reported, not asserted**. Pinning them would turn an unrelated administrative change into
a package regression. What is asserted is the behaviour the package is responsible for.

## Deliberately not covered

**Writes.** This suite is read-only. The write path — `Prefer: return=representation`, the `204`
branch, `SystemId` read-back — is exercised against the same tenant by the consuming application's
own live suite, and adding a second writer to a shared sandbox buys less than it risks. If this
project ever grows a write fact, it needs the marker-prefix and sweep-before-run discipline that
suite already uses; do not add one without it.
