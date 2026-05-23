# KRINT — Claude workflow rules

Never push to `main`. Every change goes: issue (labeled) → branch → PR (labeled) → squash-merge → delete branch.

## Branch

`feature/<issue#>_PascalCase` (new / refactor / docs) or `fix/<issue#>_PascalCase` (bugs).
Example: `fix/8_DockerSocketPermission`.

## PR

- Title: short imperative.
- Body: one-paragraph summary + `Closes #<issue>`. Nothing else — no test plans, no `##` subsections.
- At least one label. Pick from `gh label list`; don't invent.

## Commit subject

Past-tense imperative, verb first:
`Added X` · `Implemented X` · `Fixed X` · `Updated X` · `Removed X`.

No `Co-Authored-By` trailers.
