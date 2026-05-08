# Contributing to Geode .NET Client

This document defines **how** we develop and merge changes. The roadmap and
architectural decisions live in [`CLAUDE.md`](./CLAUDE.md); this file covers
process only.

> Project language is **English** for code, commits, branches, PRs, and
> issues.

---

## 1. Branching model

We use a simplified GitFlow with three roles:

```
                    ┌──────────────┐
                    │     main     │  release-ready, tagged, NuGet source
                    │  (protected) │
                    └──────▲───────┘
                           │  PR (release cut)
                    ┌──────┴───────┐
                    │   develop    │  integration branch, day-to-day target
                    └──────▲───────┘
                           │  PR (squash)
                  ┌────────┴────────┐
                  │   feat/phase-N  │  one feature branch per change
                  │   fix/...       │
                  │   docs/...      │
                  │   chore/...     │
                  └─────────────────┘
```

### 1.1 `main`

- **Protected.** No direct pushes. PR-only.
- Gets content **only** by merging `develop` (release cuts).
- Tags (`v*.*.*`, `v*.*.*-*`) are pushed from `main` and trigger
  `release.yml`.
- Linear history (squash / rebase only — merge commits are blocked by
  branch protection).
- Crosses the USB boundary into the intranet (see §6).

### 1.2 `develop`

- Day-to-day integration branch. **All feature work targets this.**
- Less ceremonial than `main` but the same commit-quality bar.
- Gets reset / merged into `main` whenever a release is cut.

### 1.3 Short-lived branches

```
feat/phase-N-<short-name>   e.g. feat/phase-1-frame-codec
fix/<short-name>            bug fixes
docs/<short-name>            documentation only
chore/<short-name>           build, deps, tooling
test/<short-name>            tests only
refactor/<short-name>        behaviour-preserving refactors
ci/<short-name>              GitHub Actions / pipeline changes
```

`ci/offline` is **reserved** for the air-gapped intranet pipeline and must
never be pushed to `origin`. See §6.

Delete the branch after the PR is merged.

---

## 2. Development principles

### 2.1 Walking skeleton, phase by phase

The project ships in 12 phases (see `CLAUDE.md`). Each phase is a vertical
slice — the code must run end-to-end against a real Geode server (or a
deterministic fixture) before the next phase begins. **Do not** build a
complete layer in isolation and stack the next layer on top.

### 2.2 One feature branch per change

A PR should be a single logical change. Mixing a refactor with a feature, or
a dependency bump with a bug fix, makes review and `git bisect` worse.
Split.

### 2.3 Read the C++ source before designing the protocol

Geode's wire protocol has no normative spec. The authoritative sources are:

- `apache/geode-native` → `cppcache/src/TcrMessage.cpp`,
  `TcrConnection.cpp`, `HandShake.cpp`, `ThinClientPoolDM.cpp`
- `apache/geode` (Java) → `geode-core` for server-side semantics

Cite the exact file and function in PR descriptions when you implement
protocol-level code. Do not paraphrase from memory.

### 2.4 Endianness and serialisation

All wire bytes are big-endian (network byte order). Use
`System.Buffers.Binary.BinaryPrimitives.*BigEndian`. Custom byte-shuffling
is grounds for a review block.

### 2.5 Tests required for protocol code

Frame codec, message types, and serialisation paths **must** have unit tests
backed by byte-level fixtures (golden bytes from `cppcache` or Wireshark
captures). Behaviour-only assertions are not enough at the wire layer.

### 2.6 Zero external runtime dependencies

The published NuGet package depends only on `Microsoft.Extensions.*`
abstractions. Adding any other runtime `PackageReference` requires a
PR-level discussion and a written justification (link the relevant section
of `CLAUDE.md` if applicable).

### 2.7 Treat warnings as errors (when CI is on)

`Directory.Build.props` sets `TreatWarningsAsErrors=true`. Do not silence
warnings with `#pragma` unless you also add a code comment explaining why.
Per-symbol `[SuppressMessage]` with a `Justification` is acceptable.

> See §5 for the current CI status — analyzer strictness is real even when
> CI is disabled, because every developer's local build enforces it.

---

## 3. Commit conventions

We follow a reduced [Conventional Commits](https://www.conventionalcommits.org/)
subset.

### 3.1 Allowed types

| Type        | Use for                                         |
| ----------- | ----------------------------------------------- |
| `feat:`     | New user-visible functionality                  |
| `fix:`      | Bug fix                                         |
| `chore:`    | Build, deps, tooling, gitignore, repo plumbing  |
| `docs:`     | Documentation only                              |
| `test:`     | Tests only (no production code change)          |
| `refactor:` | Behaviour-preserving refactor                   |
| `ci:`       | GitHub Actions or pipeline changes              |

### 3.2 Format

```
<type>: <imperative summary, lowercase, no trailing period>

<optional body explaining the why, wrapped at ~72 chars>

<optional trailers, e.g. Refs: #12, BREAKING CHANGE: ...>
```

Examples:

```
feat: add big-endian binary writer for frame codec
fix: handle short read in message header parser
chore: bump xunit.v3 to 1.0.1
ci:   re-enable build-test workflow
```

### 3.3 Linking issues

Reference the issue in the commit body **or** the PR description, not the
summary line. Use `Refs: #N` for context, `Closes #N` to auto-close on
merge.

---

## 4. Pull request and merge rules

### 4.1 Targets

| Source                | Target    | Purpose                              |
| --------------------- | --------- | ------------------------------------ |
| `feat/*` `fix/*` etc. | `develop` | normal day-to-day work               |
| `develop`             | `main`    | release cut (see §7)                 |

Never open a PR from `feat/*` directly to `main` unless it is a hotfix that
must skip `develop` (and even then, back-port to `develop` immediately).

### 4.2 PR template is mandatory

Fill in every section of `.github/pull_request_template.md`:

- **Phase** — which phase from the roadmap (or `N/A` for chores).
- **Changes** — bullet list of what changed.
- **Tests** — what you added / why existing coverage is enough.
- **Notes for reviewer** — open questions, things to look at first.

### 4.3 Self-review before merging

Open the PR's **Files changed** tab and read it as if you were a reviewer.
Most stylistic / leftover-debug-print issues catch themselves this way.

### 4.4 Approval policy (solo-dev mode)

While the project has only one regular contributor, the author may
self-merge once the build is green and the self-review pass is done.
Branch protection on `main` requires the PR to exist; it does not require
an approver count > 0.

When a second regular contributor joins, raise
`required_approving_review_count` to `1` via `gh api PUT
repos/.../branches/main/protection`.

### 4.5 Merge mode

| Target    | Allowed merge mode | Why                                    |
| --------- | ------------------ | -------------------------------------- |
| `develop` | Squash             | one PR = one commit on `develop`       |
| `main`    | Squash *or* Rebase | linear history is enforced by branch   |
|           |                    | protection (`required_linear_history`) |

The squashed commit message must follow §3.

### 4.6 Build must be green (when CI is on)

When CI is enabled, the `build-test` job must be green before merge. Do
not bypass red checks. If CI is flaky, fix the flake — do not re-run
until green.

### 4.7 Delete the branch after merge

Both locally (`git branch -d feat/...`) and on origin. GitHub can do this
automatically — leave the **"Automatically delete head branches"** repo
setting on.

---

## 5. CI status

> **CI is currently DISABLED during the MVP phases.**

`.github/workflows/ci.yml` is fully commented out. The `release.yml`
workflow is left intact but only triggers on `v*.*.*` tag pushes, so it
cannot fire accidentally during normal work.

### 5.1 Why disabled

`Directory.Build.props` sets `AnalysisLevel=latest-recommended` plus
`TreatWarningsAsErrors=true`. The Phase 0 skeleton itself violates several
opinionated analyzer rules (CA1848, CA1711, ...). A push / red CI / fix /
push loop has no useful signal at this stage and only wastes CI minutes.

### 5.2 When to re-enable

At the **latest**, before Phase 5 / first NuGet preview release. Earlier
is fine if the analyzer strictness has been settled (either relax to
`latest-default`, or pre-fix every violation in the skeleton).

### 5.3 How to re-enable

1. Uncomment the body of `.github/workflows/ci.yml`.
2. Push the change as `ci: re-enable CI workflow`.
3. Add `build-test` to `main`'s required status checks:
   ```bash
   gh api -X PATCH repos/TomiCheng/GeodeSharp/branches/main/protection/required_status_checks \
     -f 'contexts[]=build-test'
   ```
   (Or via the GitHub UI: **Settings → Branches → main → Edit → Status
   checks**.)

---

## 6. Dual-network workflow

The maintainer (`Tomi`) develops on two networks:

- **Internet side** — `origin` on GitHub, public CI, NuGet publish.
- **Intranet side** — air-gapped enterprise GitLab / GitHub, internal CI.

Sync is one-way: `main` on the internet → USB bare repo → intranet.

Rules:

1. Only **reviewed and merged** commits on `main` cross the USB boundary.
2. `develop`, feature branches, and PR branches **do not** cross. The
   intranet has no business seeing WIP.
3. The branch `ci/offline` carries intranet-only CI/CD configuration. It
   **must never** be pushed to `origin` (the public GitHub remote).
4. Intranet-side commits stay intranet-side. They are not back-ported to
   `origin` unless explicitly cleaned and re-authored as a public PR.

---

## 7. Releasing

Releases are tag-driven. The procedure:

1. Open a release PR: `develop` → `main`. Title: `release: vX.Y.Z[-pre]`.
2. Self-review the diff (everything that has accumulated on `develop`
   since the last release tag).
3. Squash-merge into `main`. The squash commit message should be
   `release: vX.Y.Z[-pre]` plus a brief change log in the body.
4. Tag `main` locally and push:
   ```bash
   git checkout main && git pull
   git tag v0.1.0-alpha
   git push origin v0.1.0-alpha
   ```
5. `release.yml` packs, pushes to NuGet, and creates a GitHub Release with
   auto-generated notes.
6. (Optional) Fast-forward `develop` to `main` so they do not diverge:
   ```bash
   git checkout develop && git merge --ff-only main && git push
   ```

Versioning follows [SemVer 2.0](https://semver.org/). Pre-1.0 the public
API may change between minor versions; we mark unstable phases with
`-alpha` / `-beta` suffixes (MinVer derives the version from the tag).

---

## 8. Where to ask

- Architecture / roadmap questions → read `CLAUDE.md` first, then open a
  GitHub Discussion or issue tagged `question`.
- Bugs → open an issue with a minimal repro.
- Protocol-level design questions → cite the `cppcache` or `geode-core`
  source you read; that is the conversation starter.
