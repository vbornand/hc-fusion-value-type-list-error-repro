# Fusion drops the error of a nulled value-type list

Minimal reproduction for HotChocolate Fusion 16.7.5 (the same code is on `main`).

Two source schemas share `Item`, which has no lookup, so Fusion composes it as a value type.
Source schema A returns `itemPage { items }`, and every item breaks its non-null `name`.
A nulls `items` and reports the HC0018 errors. The gateway returns the null list with no error,
so a client cannot tell an empty result from a failure.

## Run

```shell
dotnet run
```

Requires the .NET 10 SDK. The program hosts source schema A on port 5201, B on 5202, and the
gateway on 5200, then sends the same query to A and to the gateway.

## Output

```text
Query: { itemPage { items { name } } }

Source schema A (http://localhost:5201/graphql):
{"errors":[{"message":"Cannot return null for non-nullable field.","path":["itemPage","items",0,"name"],"extensions":{"code":"HC0018"}}],"data":{"itemPage":{"items":null}}}

Gateway (http://localhost:5200/graphql):
{"data":{"itemPage":{"items":null}}}
```

Expected: the gateway keeps the error, for example with the path `["itemPage","items"]`.

## What matters

- `Item` must be a value type: shared by several source schemas, with no lookup. With an entity
  type, or a type only A has, the gateway keeps the error.
- The list must sit below the root. With `items` directly on `Query`, the error is pocketed and
  then promoted to `["items"]`.

## Rebuilding gateway.far

`gateway.far` is committed. To compose it again from the source schemas:

```shell
dotnet run -- export
nitro fusion compose -f A -f B -a gateway.far
```
