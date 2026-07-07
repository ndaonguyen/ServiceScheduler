# Input: <feature name>

> **Required input** for `/plan-issue`. The GitHub issue is still the source of truth for acceptance criteria; this file adds design links, brainstorming, and constraints the issue doesn't capture.
>
> Filename convention: `<issue-number>-<short-slug>.md` (e.g. `1234-widget-search.md`) so `/plan-issue <issue-number>` can auto-discover it.
>
> **Required sections** (skill will refuse to plan without them): §1 Issue, §2 Design / Reference Links, §4 Constraints & Non-goals. §3 Brainstorming may be left empty.

## 1. Issue
- Primary: `#<NNNN>` — <URL>
- Related (optional): `#<NNNN>`, `#<NNNN>`

## 2. Design / Reference Links
- <Figma / mockup / spec URL>
- <API contract, external doc>

## 3. Brainstorming
Free-text: proposed approach, payload shapes, reuse hints, known edge cases, integration notes.

Informs planning but **does not override** the issue's acceptance criteria. Conflict order (highest wins):
1. Issue acceptance criteria + decisions in issue comments
2. Design/reference links
3. Brainstorming notes

## 4. Constraints & Non-goals
- Constraints: <deadlines, compat, perf, security>
- Non-goals: <explicit exclusions>

---
Delete or move to an `archive/` subfolder once the plan PR merges.
