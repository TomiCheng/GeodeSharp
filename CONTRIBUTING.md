# Contributing to Geode .NET Client

This document defines how we develop and merge changes in this repository.
Read it before opening your first PR. The roadmap and architectural decisions
live in [`CLAUDE.md`](./CLAUDE.md); this file covers **process only**.

> Project language is **English** for code, commits, branches, PRs, and issues.

---

## 1. Development principles

### 1.1 Walking skeleton, phase by phase

The project ships in 12 phases (see `CLAUDE.md`). Each phase is a vertical
slice — the code must run end-to-end against a real Geode server (or a
deterministic fixture) before the next phase begins. **Do not** build a
complete layer in isolation and stack the next layer on top.

### 1.2 One feature branch per phase / change

```
feat/phase-N-<short-name>     # e.g. feat/phase-1-frame-codec
fix/<short-name>              # bug fixes
docs/<short-name>              # documentation only
chore/<short-name>             # build, deps, tooling
test/<short-name>              # tests only
refactor/<short-name>          # behaviour-preserving refactors
ci/<short-name>                # GitHub Actions / pipeline changes
```

`ci/offline` is **reserved** for the air-gapped intranet pipeline and must
never be pushed to `origin`. See §4.

### 1.3 Read the C++ source before designing the protocol

Geode's wire protocol has no normative spec. The authoritative sources are:

- `apache/geode-native` → `cppcache/src/TcrMessage.cpp`,
  `TcrConnection.cpp`, `HandShake.cpp`, `ThinClientPoolDM.cpp`
- `apache/geode` (Java) → `geode-core` for server-side semantics

Cite the exact file and function in PR descriptions when you implement
protocol-level code. Do not paraphrase from memory.

### 1.4 Endianness and serialisation

All wire bytes are big-endian (network byte order). Use
`System.Buffers.Binary.BinaryPrimitives.*BigEndian`. Custom byte-shuffling
is grounds for a review block.

### 1.5 Tests required for protocol code

Frame codec, message types, and serialisation paths **must** have unit tests
backed by byte-level fixtures (golden bytes from `cppcache` or Wireshark
captures). Behaviour-only assertions are not enough at the wire layer.

### 1.6 Zero external runtime dependencies

The published NuGet package depends only on `Microsoft.Extensions.*`
abstractions. Adding any other runtime `PackageReference` requires a PR-level
discussion and a written justification (link the relevant section of
`CLAUDE.md` if applicable).

### 1.7 Treat warnings as errors

`Directory.Build.props` sets `TreatWarningsAsErrors=true`. Do not silence
warnings with `#pragma` unless you also add a code comment explaining why.

---

## 2. Commit conventions

We follow a reduced [Conventional Commits](https://www.conventionalcommits.org/)
subset.

### 2.1 Allowed types

| Type       | Use for                                         |
| ---------- | ----------------------------------------------- |
| `feat:`    | New user-visible functionality                  |
| `fix:`     | Bug fix                                         |
| `chore:`   | Build, deps, tooling, gitignore, repo plumbing  |
| `docs:`    | Documentation only                              |
| `test:`    | Tests only (no production code change)          |
| `refactor:`| Behaviour-preserving refactor                   |
| `ci:`      | GitHub Actions or pipeline changes              |

### 2.2 Format

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
```

### 2.3 Linking issues

Reference the issue in the commit body **or** the PR description, not the
summary line. Use `Refs: #N` for context, `Closes #N` to auto-close on merge.

---

## 3. Pull request and merge principles

### 3.1 No direct commits to `main`

`main` is protected. All changes land via PR. The only exception is the
initial root commit of the repository.

### 3.2 PR template is mandatory

Fill in every section of `.github/pull_request_template.md`:

- **Phase** — which phase from the roadmap (or `N/A` for chores).
- **Changes** — bullet list of what changed.
- **Tests** — what you added / why existing coverage is enough.
- **Notes for reviewer** — open questions, things to look at first.

### 3.3 CI must be green

`build-test` (the CI job) is a required status check. Do not merge red PRs.
If CI is flaky, fix the flake — do not re-run until green.

### 3.4 One PR = one logical change

Mixing a refactor with a feature, or a dependency bump with a bug fix, makes
review and `git bisect` worse. Split.

### 3.5 Squash merge only

We keep `main` linear. Settings:

- **Squash merge:** allowed (default)
- **Rebase merge:** allowed
- **Merge commits:** disabled

The squashed commit message must follow §2.

### 3.6 Self-review before requesting review

Open the PR's **Files changed** tab and read it as if you were a reviewer.
Most stylistic / leftover-debug-print issues catch themselves this way.

### 3.7 Solo-dev approval policy

Until the project has more than one regular contributor, the author may
self-merge once CI is green and the self-review pass is done. Once a second
contributor is regular, switch to "require 1 approval" via branch protection.

---

## 4. Dual-network workflow

The maintainer (`Tomi`) develops on two networks:

- **Internet side** — `origin` on GitHub, public CI, NuGet publish.
- **Intranet side** — air-gapped enterprise GitLab/GitHub, internal CI.

Sync is one-way: `main` on the internet → USB bare repo → intranet.

Rules:

1. Only **reviewed and merged** commits on `main` cross the USB boundary.
2. The branch `ci/offline` carries intranet-only CI/CD configuration.
   It **must never** be pushed to `origin` (the public GitHub remote).
3. Intranet-side commits stay intranet-side. They are not back-ported to
   `origin` unless explicitly cleaned and re-authored as a public PR.

---

## 5. Releasing

Releases are tag-driven. To cut a release:

1. Ensure `main` is green.
2. Tag locally: `git tag v0.1.0-alpha && git push origin v0.1.0-alpha`.
3. The `release.yml` workflow packs, pushes to NuGet, and creates a GitHub
   Release with auto-generated notes.

Versioning follows [SemVer 2.0](https://semver.org/). Pre-1.0 the public API
may change between minor versions; we mark unstable phases with
`-alpha` / `-beta` suffixes (MinVer handles this from the tag).

---

## 6. Where to ask

- Architecture / roadmap questions → read `CLAUDE.md` first, then open a
  GitHub Discussion or issue tagged `question`.
- Bugs → open an issue with a minimal repro.
- Protocol-level design questions → cite the `cppcache` or `geode-core`
  source you read; that is the conversation starter.
