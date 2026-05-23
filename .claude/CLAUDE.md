# KRINT — Claude workflow rules (enforced)

## Branch + PR flow

**Never work on `main`.** Every change goes through an issue → branch → PR.

1. **Open a GitHub issue first** (or pick an existing one). Issue must carry a label — see `gh label list` for the valid set; never invent a new label.
2. **Branch name:** `feature/<issue#>_PascalCase` for new capabilities / refactors / docs, `fix/<issue#>_PascalCase` for bugfixes. The slash-prefix is the only thing that varies; the number is the issue this PR closes; the PascalCase suffix is a short summary (no hyphens / underscores between words).
   - ✅ `feature/12_DropKrintAcronym`
   - ✅ `fix/8_DockerSocketPermission`
   - ❌ `selfhost-fixes`, `chore/drop-krint-acronym`, `docs/byo-oidc`
3. **PR title:** short imperative — same style as commit subjects.
4. **PR body — only two things:**
   - One-paragraph Summary (or a tight bullet list if it spans multiple concerns).
   - `Closes #<issue>` line.
   - **No test plan.** No "## Test plan" / `- [x]` checklists.
5. **PR must carry at least one label.** Pass `--label <name>` on `gh pr create`; never open unlabeled.
6. **Merge:** squash-merge, delete branch (remote + local). Sync local `main` via `git fetch --prune && git reset --hard origin/main`.

## Commit messages

**Subject = short imperative starting with a verb in past tense.** Pattern to default to:

- `Implemented <thing>` — for new functionality
- `Fixed <thing>` or `Fixed <thing> problem` — for bug fixes
- `Updated <thing>` — for non-bug changes to existing behavior
- `Removed <thing>` — for deletions

Drift is allowed when the pattern reads awkwardly, but stay imperative-past and keep the subject under ~72 chars. Examples that fit:

- `Implemented BYO OIDC provider docs`
- `Fixed Docker socket permission on Windows`
- `Updated release-drafter to include documentation + CI/CD`

Body (when one is needed) is plain prose or bullets explaining the **why**, not the what. No `Co-Authored-By` lines.

## Labels in scope (KRINT)

`feature`, `enhancement`, `bug`, `refactor`, `documentation`, `CI/CD` are all in the release-drafter changelog. `duplicate` is administrative — never use it on a real PR.

If multiple labels apply, pass them comma-separated: `--label feature,documentation`.

## Anti-checklist (don't do this)

- ❌ Committing or pushing directly to `main`.
- ❌ Opening a PR without a linked issue.
- ❌ Opening a PR or issue without a label.
- ❌ Branch names without the `<type>/<issue#>_PascalCase` shape.
- ❌ PRs with a "Test plan" section.
- ❌ Verbose multi-section PR bodies (Why / Changes / Test plan / Notes / etc.).
- ❌ `Co-Authored-By: Claude ...` trailers on commits.
- ❌ Inventing new labels.
