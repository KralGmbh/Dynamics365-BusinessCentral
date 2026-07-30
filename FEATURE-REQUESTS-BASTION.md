# Feature requests — typed query ergonomics

Two small additions to the typed query surface, from migrating a production consumer
(Bastion) off hand-maintained wire-name constants and onto the 2.0 expression-based API.

Unlike the other `*-BASTION.md` documents in this repo, this one reports no defect and no
tenant observation. Both requests are ergonomic, both are additive, and neither changes
existing behaviour.

Line references are against `2.0.0-alpha.4`.

---

## Context: the problem 2.0 already solved

Worth stating first, so these requests are not read as asking for something that exists.

The consumer maintained, per entity, a `*Fields` static class of wire-name constants
referenced from `[JsonPropertyName]` attributes and from every `$filter` and `$select` call
site — 12 classes, 101 constants, 99 references. 2.0 already makes all of it deletable:

- `BusinessCentralField.Of<T>(x => x.Prop)` resolves a selector to the wire name.
- Every `Filter` operator has an `Expression<Func<TEntity, object?>>` overload.
- `Query<T>()` resolves the entity path from `[BusinessCentralEntity]`, so path constants go too.
- `Select`, `OrderBy` and `Expand` on the fluent builder are already expression-based.

**No change is requested for any of that.** The two requests below are the gaps that remain
visible once a consumer actually adopts it.

---

## F1 — Let the fluent builder infer the entity type in filters

**Kind: ergonomics · Additive**

`IBusinessCentralQuery<TEntity>` knows its entity type, but `Where` takes an already-built
`ODataFilter`, and `Filter`'s typed overloads are static — so the type argument has to be
restated at every operator, inside a query that has already stated it:

```csharp
await _client.Query<ProdOrderComponent>()
    .Where(Filter.Equals<ProdOrderComponent>(x => x.ProductionOrderNumber, orderNo)
      .And(Filter.Equals<ProdOrderComponent>(x => x.ProdOrderLineNumber, lineNo))
      .And(Filter.StartsWith<ProdOrderComponent>(x => x.ItemNumber, "EB")))
    .Select(x => x.ItemNumber)
    .ToListAsync(ct);
```

Three restatements of `ProdOrderComponent` in one query. It also reads inconsistently against
the neighbouring `Select(x => x.ItemNumber)`, which infers the same type from the same builder.

### Proposal

A `Where` overload taking a filter builder bound to `TEntity`:

```csharp
IBusinessCentralQuery<TEntity> Where(Func<IFilterBuilder<TEntity>, ODataFilter> build);
```

```csharp
await _client.Query<ProdOrderComponent>()
    .Where(f => f.Equals(x => x.ProductionOrderNumber, orderNo)
                 .And(f.Equals(x => x.ProdOrderLineNumber, lineNo))
                 .And(f.StartsWith(x => x.ItemNumber, "EB")))
    .Select(x => x.ItemNumber)
    .ToListAsync(ct);
```

`IFilterBuilder<TEntity>` mirrors `Filter`'s operator set with the type argument fixed, and
returns the same `ODataFilter`, so `.And` / `.Or` composition and everything downstream are
unchanged. It is a thin forwarding layer over the existing typed overloads — no new rendering
logic, no new resolution rules, nothing to keep in sync beyond the operator list.

The existing `Where(ODataFilter)` and `Where(string)` overloads stay; this is one more way in,
not a replacement.

### Acceptance criteria

- [ ] `Where(Func<IFilterBuilder<TEntity>, ODataFilter>)` exists on the fluent builder.
- [ ] `IFilterBuilder<TEntity>` covers every operator `Filter` exposes, including `In`,
      `IsNull` and `IsNotNull`.
- [ ] A test asserts a builder-composed filter renders identically to the equivalent
      `Filter.X<TEntity>(...)` chain.
- [ ] README shows the builder form as the default for `Query<T>()`.

---

## F2 — Default `$select` to the entity's declared properties

**Kind: correctness + ergonomics · Behaviour change, see the note below**

A query with no `Select(...)` emits no `$select`, so Business Central returns every column of
the entity set. The consumer then deserializes into a type that declares six of them and
discards the rest — including, on wide entity sets, columns it has no property for at all.

Where the entity class *is* the projection — which is the normal case, since these classes are
written per use — the class already states exactly what should be selected. Consumers restate
it anyway:

```csharp
select: [ProductionOrderLineFields.ItemNumber, ProductionOrderLineFields.LineNumber]
```

21 such lists in this consumer, every one of them a subset of properties the class already
declares, kept in sync by hand.

### Proposal

When no explicit projection is given, derive `$select` from `TEntity`'s declared, serializable
properties, resolved through the same `PropertyPath` rules as everything else.

Note the direction: this can only ever **narrow** what is requested. Today's default is "all
columns"; the proposed default is "the columns the type can actually hold". No consumer can
receive less data than their type declares, so nothing that currently deserializes can stop
deserializing.

An explicit `Select(...)` still wins, which keeps the wide-shared-entity case working — a
consumer with one broad `Item` class and narrow per-call projections is unaffected, because
those calls already pass a projection.

**This is a behaviour change**, so it wants an escape hatch and a changelog line, not a silent
flip. An opt-out (`.SelectAll()`, or an option on registration) covers the case where an entity
type is deliberately partial and the caller wants the full row for logging or diagnostics.

### Acceptance criteria

- [ ] With no `Select(...)`, `$select` lists `TEntity`'s serializable properties.
- [ ] An explicit `Select(...)` overrides it entirely.
- [ ] An opt-out exists for requesting all columns.
- [ ] A test asserts the derived `$select` matches the type's `[JsonPropertyName]` values,
      including nested navigation properties.
- [ ] Documented in the changelog as a behaviour change, with the narrowing-only argument stated.

---

## Appendix: provenance

Both requests come from converting Bastion's Business Central access layer from 1.0-style
string constants to the 2.0 typed API — 12 entity types, 99 call sites. F1 is the friction
that shows up on every multi-operator filter once the typed overloads are adopted. F2 is the
last piece of duplication that survives the conversion: the projection stated once in the
class and again at each call site.

Neither is blocking. Both were worth writing down while the conversion was fresh.
