# docs/

Two kinds of documents live here — keep them separate.

## Reference docs (flat, named by subject)

Describe how the system **is**. Long-lived, edited in place as the system changes.

- [authentication.md](authentication.md) — JWT-cookie auth design
- [database.md](database.md) — schema, EF migrations, persistence
- [gap.md](gap.md)

## Artifact docs (subfoldered, named by ID)

Describe a **change** at a point in time. One file per issue, mostly append-only.

| Folder | Contents | Produced by | Naming |
|---|---|---|---|
| `prds/` | Product requirement docs | `/to-prd` | `<issue-number>-<slug>.md` |
| `plans/` | Implementation plans | `/plan-issue` | `<issue-number>-<slug>.md` |
| `adrs/` | Architecture decision records | `/grill-with-docs` | `<adr-number>-<slug>.md` |
| `inputs/` | Required input for `/plan-issue` — design links, brainstorming, constraints | hand-written | `<issue-number>-<slug>.md` |

The issue number is the durable link across a change's lifecycle: PRD → plan → PR → merged code. To find everything about issue #1234: `docs/**/1234-*.md`.

### Archiving

When work ships, `git mv` the file into an `archive/` subfolder under its type (create on first use). Keeps the active list short without losing history.
