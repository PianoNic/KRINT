# KRINT — Claude workflow rules

Never push to `main`. Every change goes:

1. **Issue** — open one (or pick existing) with at least one label from `gh label list`.
2. **Branch** — `feature/<issue#>_PascalCase` for new / refactor / docs, `fix/<issue#>_PascalCase` for bugs.
3. **PR** — short imperative title; body is one-line summary + `Closes #<issue>`; at least one label.
4. **Squash-merge + delete branch**, then `git fetch --prune && git reset --hard origin/main`.

## Branch naming

- ✅ `feature/12_DropKrintAcronym`
- ✅ `fix/8_DockerSocketPermission`

## PR body

One line in commit-subject style, then `Closes #<issue>`. The commits already say what changed — the PR doesn't need to repeat them.

```
Implemented external OIDC provider docs

Closes #11
```

## Commit subject

Past-tense imperative, verb first:

- `Added <thing>`
- `Implemented <thing>`
- `Fixed <thing>`
- `Updated <thing>`
- `Removed <thing>`

Examples:

- `Added Buy Me a Coffee link to the content header`
- `Implemented BYO OIDC provider docs`
- `Fixed Docker socket permission on Windows`

No `Co-Authored-By:` trailers.

## Labels

Pick from `gh label list`. KRINT's active set: `feature`, `enhancement`, `bug`, `refactor`, `documentation`, `CI/CD`. Multiple: `--label feature,documentation`.
