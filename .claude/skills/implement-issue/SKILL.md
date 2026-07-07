---
name: implement-issue
description: Take a GitHub issue that already has a merged plan under docs/plans/, create the implementation branch, drive TDD through the plan section by section, and open a feature PR that closes the issue. Use when the user wants to implement (not plan) a ticket.
argument-hint: "<issue-url-or-number>"
---

# /implement-issue

Turn a **planned issue** into a **feature PR**. Assumes `/plan-issue` already produced `docs/plans/<n>-*.md` and its plan PR was merged. Refuses to run otherwise — plan first.

Do not invent scope. The plan is the contract; if reality forces deviation, stop and ask.

---

## Step 0: Preflight

Run in parallel:

- `gh auth status` — must be authenticated.
- `git status` — must be clean; if dirty, stop and ask user to stash/commit.
- `git rev-parse --abbrev-ref HEAD` — record; you'll branch off this.

If any fails, stop.

---

## Step 1: Resolve issue + plan

Accept a URL or bare number. Then:

1. Fetch the issue: `gh issue view <n> --json number,title,body,url,state`.
   - If state is `closed`, ask user whether to proceed anyway.
2. Find the plan file: `docs/plans/<n>-*.md`.
   - **None** → stop: *"No plan found. Run `/plan-issue <n>` first."*
   - **Multiple** → ask user which to use.
3. Confirm the plan PR was merged:
   ```
   gh pr list --search "docs/plans/<n>- in:files" --state merged
   ```
   If nothing merged, ask user whether to proceed with an unapproved plan.
4. Read the plan file. Extract:
   - **Branch name** and **PR title** from the "Branch & PR" section.
   - Ordered list of **Changes** (with their "Pattern to mimic" snippets).
   - **Acceptance criteria** from the traceability table.
   - Whether entities changed (migration needed).
   - Whether template parameterization (`#if UseApiOnly`) is in scope.

---

## Step 2: Load repo context

- Read [CLAUDE.md](CLAUDE.md).
- If the plan is auth-adjacent, read [docs/authentication.md](docs/authentication.md).
- Read `docs/inputs/<n>-*.md` if present (extra constraints).

---

## Step 3: Set up the branch

```bash
git checkout main && git pull
git checkout -b <branch-name-from-plan>
```

If a branch with that name already exists locally or remotely, ask user whether to reuse (continue) or pick a new name.

---

## Step 4: Drive TDD, section by section

For each change in the plan (in order):

1. **Load the "Pattern to mimic" snippet** — open the sibling file the plan cited; treat it as the template.
2. **Invoke the `tdd` skill** for this change:
   - Red: write the failing test the plan's traceability table called out.
   - Green: make it pass with code modeled on the pattern.
   - Refactor.
3. Mark the change done in your working list.
4. Commit at natural boundaries (per-change or per-slice, not one giant commit). Conventional Commits: `feat: …`, `test: …`, `refactor: …`.

**Rules while implementing:**
- Do not silently expand scope. If a change needs something not in the plan, stop and ask — offer to add it (with user approval) or defer as a follow-up.
- If the plan says "no existing analogue" for a change, that's a red flag — surface it before writing the code, not after.
- If entities changed, add the migration **once**, before the final verification pass:
  ```
  dotnet ef migrations add <DescriptiveName> \
    --project source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api
  ```
