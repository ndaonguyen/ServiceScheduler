# Plan: <Issue Title> (#<issue-number>)

> **Issue**: <issue URL>
> **Standalone**: this plan is executable without reading any other file.

## Goal
One sentence: what changes and why.

## Scope & Non-goals
- In scope: <explicit bullets>
- Out of scope: <explicit bullets>

## Requirement Traceability
| Acceptance criterion | Plan section | Verification |
|---|---|---|
| AC-1 … | Changes → <section> | <test name / manual check> |

## Changes

### Domain / Application / Infrastructure / Api / ClientApp
- **File**: `source/…/Foo.cs` — <what to add/modify>
- **New file**: `source/…/Bar.cs` — <purpose>
- **EF migration** (if entities change): `dotnet ef migrations add <Name> --project source/ServiceScheduler.Infrastructure --startup-project source/ServiceScheduler.Api`

<Code snippets showing key signatures/contracts only — not full implementation.>

## Key Files
- `source/…/…` — <why touched>
- `tests/…/…` — <new/updated tests>

## Testing & Verification
- `dotnet build -c Release` passes
- `dotnet test -c Release` passes
- New tests: <names + what they cover>
- Manual check: <curl / UI step>

## Template parameterization (if applicable)
