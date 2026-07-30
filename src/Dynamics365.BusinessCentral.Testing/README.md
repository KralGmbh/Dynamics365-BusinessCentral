# Dynamics365.BusinessCentral.Testing

Test doubles for [Dynamics365.BusinessCentral](https://www.nuget.org/packages/Dynamics365.BusinessCentral).

The highest-risk part of using an OData client is the OData it generates — filter
rendering, `$select` field names, casing, paging. Mocking `IBusinessCentralClient` can't
test any of that: a mock verifies *that* a query happened, not *what it asked for*.

`FakeBusinessCentral` takes the opposite approach: it runs a **real**
`BusinessCentralClient` over a scripted transport. URL building, filter rendering, paging,
retry and deserialization execute exactly as in production; you script the HTTP responses
and assert on the requests that were actually produced.

```csharp
using var bc = new FakeBusinessCentral();
bc.EnqueuePage(new Item { No = "X", Description = "Pump" });

var items = await bc.Client.QueryAsync<Item>("items",
    Filter.Equals("no", "X"), select: ["no", "description"]);

Assert.Equal(
    "/Company('TEST')/items?$filter=no eq 'X'&$select=no,description",
    bc.Requests.Single().DecodedPathAndQuery);
```

## Scripting

Responses are consumed in order, one per request. Token acquisition is answered
automatically (and counted in `TokenRequestCount`, not recorded), so tests never script
auth. An unscripted request throws with a message naming it — no silent empty results.

```csharp
bc.EnqueuePage(rows)                                   // {"value":[...]}
bc.EnqueuePage(page1, nextLink: "page2")               // server-driven paging
bc.EnqueuePage(rows, totalCount: 42)                   // @odata.count
bc.EnqueueEntity(entity)                               // GET by key / write echo
bc.EnqueueNoContent()                                  // 204 on a write
bc.EnqueueError(HttpStatusCode.TooManyRequests,
    retryAfter: TimeSpan.FromSeconds(5))               // typed exception paths
bc.EnqueueNetworkFailure()                             // BusinessCentralConnectionException
bc.Enqueue(req => ...)                                 // anything else
```

Failure scripting means `catch` branches are testable without constructing exception types
by hand — the real `BusinessCentralExceptionFactory` maps the scripted status code to the
right `BusinessCentralException` subtype.

## What this can and cannot prove

`FakeBusinessCentral` proves **your half** of the contract: that your code produces the
OData you intend. It cannot prove **Business Central's half** — that the server accepts it.
A real example from a production consumer: a test asserting
`$filter=no in ('EBH100','EBT200')` passed, while the live tenant rejected that exact
filter with `BadRequest_MethodNotImplemented`, because BC does not support the `in`
operator without `$schemaversion=2.1`. The fake answers whatever it is scripted to answer.

Treat wire-level compatibility as a separate concern: verify operators against a live
tenant once, then let these tests guard against regressions in what you *generate*.

## Assertions

`Requests` records every data request in order: `Method`, `Uri`, `Body`, `PathAndQuery`
(as sent on the wire) and `DecodedPathAndQuery` (percent-decoding undone, for readable
assertions).

## Configuration

Defaults: company `TEST`, base URL `https://bc.test`, instant deterministic retries.
Override anything via the constructor:

```csharp
using var bc = new FakeBusinessCentral(o => o.Company = "CRONUS AG");
```

For DI-level tests, wire `bc.Handler` in as the primary handler instead of using
`bc.Client`:

```csharp
services.AddBusinessCentral(...);
services.AddHttpClient(BusinessCentralHttpClients.Client)
        .ConfigurePrimaryHttpMessageHandler(() => bc.Handler);
```

Versioned in lockstep with the main package. Full documentation:
https://github.com/KralGmbh/Dynamics365-BusinessCentral
