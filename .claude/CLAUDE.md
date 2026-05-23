# KRINT — Claude workflow rules

Never push to `main`. Every change goes:

1. **Issue** — open one (or pick existing). Must carry at least one label from `gh label list`.
2. **Branch** — `feature/<issue#>_PascalCase` for new / refactor / docs, `fix/<issue#>_PascalCase` for bugs.
3. **PR** — short imperative title; body is *summary + `Closes #<issue>`* only; at least one label.
4. **Squash-merge + delete branch** (remote + local). Sync `main` with `git fetch --prune && git reset --hard origin/main`.

## Branch naming

The slash-prefix is the only thing that varies between the two shapes. PascalCase suffix is a short summary — no hyphens, no underscores between words.

- ✅ `feature/12_DropKrintAcronym`
- ✅ `feature/15_TightenReadmeIntro`
- ✅ `fix/8_DockerSocketPermission`
- ❌ `selfhost-fixes` (no type prefix, no issue number, kebab-case)
- ❌ `chore/drop-krint-acronym` (`chore/` is not a valid type; kebab-case)
- ❌ `feature/document-byo-oidc` (missing issue number, kebab-case)

## PR body

Two things, in order:

1. One-paragraph summary (or a tight bullet list if it genuinely spans multiple concerns).
2. `Closes #<issue>`.

**No** `## Test plan` section, `## Why` / `## Changes` / `## Notes` headers, or `- [x]` checklists. The local verification step is yours — it doesn't belong on the PR.

Example of a good PR body:

```
Adds a Buy Me a Coffee link next to the GitHub icon in the content header.
Uses simpleBuymeacoffee from the already-installed @ng-icons/simple-icons
package, so no new deps.

Closes #14
```

## Commit subject

Past-tense imperative, verb first. Default verbs:

- `Added <thing>` — small additions (a config option, missing field, single file)
- `Implemented <thing>` — new functionality of meaningful size
- `Fixed <thing>` (or `Fixed <thing> problem`) — bug fixes
- `Updated <thing>` — non-bug changes to existing behavior
- `Removed <thing>` — deletions

Keep under ~72 chars. Drift is allowed when the pattern reads awkwardly, but stay verb-led and past-tense. The body (when one is needed) explains the **why**, not the what.

Examples that fit:

- `Added Buy Me a Coffee link to the content header`
- `Implemented BYO OIDC provider docs`
- `Fixed Docker socket permission on Windows`
- `Updated release-drafter to include documentation + CI/CD`
- `Removed K/R/I/N/T tagline from README and SPA`

**Never** add `Co-Authored-By:` trailers — the user is the sole author.

## Labels

Pick from `gh label list` (authoritative — don't memorise here). For KRINT today the active set is `feature`, `enhancement`, `bug`, `refactor`, `documentation`, `CI/CD`. Pass multiple comma-separated if accurate: `--label feature,documentation`. Never invent a new label without asking first.
