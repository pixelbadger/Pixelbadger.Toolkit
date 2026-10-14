# Environment Checks

Prove the environment can carry the run to the Completion Gate, in stages, each run at the point it first matters. A run that discovers mid-integration that Docker is missing or the disk is full has wasted every stream before it.

## Protocol

Every check follows the same protocol:

1. **Detect.** Run the check's probe.
2. **Heal once.** If it fails, make **one** attempt from that check's heal allow-list. Nothing outside the allow-list.
3. **Re-check.** Run the probe again.
4. **Record** the result as one of:
   - `pass`: passed first time;
   - `healed: <action>`: passed after the named heal;
   - `raised`: still failing, or no heal is allowed. A raised check is an operator question before that stage's gate. Ask it as a Zero-Ambiguity Gate question, with the evidence and your recommendation.

Heal rules:

- A heal never mutates (see the mutation definition in SKILL.md). It never touches tracked files, and never changes the operator's checkout.
- A heal never deletes anything the swarm didn't create. Deletion is limited to `swarm/*` worktrees and branches, swarm build outputs, swarm scratch, and evidence dirs.
- A heal never stops a process the swarm didn't start.
- If a heal would need anything else, the check is `raised`.

## Stage A — host

**When:** at invocation, before research.

| Check | Probe / pass condition | Heal allow-list |
|---|---|---|
| **Free disk and memory snapshot** | Record free disk on the volumes for worktrees, scratch and evidence, and free memory. Pass if there is enough for at least one worktree, its build and the baseline test run. | Remove stale `swarm/*` worktrees and branches from earlier runs (`git worktree prune`, then `git worktree remove`), their build outputs, and stale evidence dirs. |
| **Base commit** | The base commit resolves (`git rev-parse <base>`) and checks out into a worktree. | Fetch the base branch once. |
| **Toolchain / SDK** | Every SDK and tool the build, tests and E2E need is present at the version the repository pins (e.g. `global.json`, `.nvmrc`, `rust-toolchain`). | None. Installing a toolchain is the operator's call: raise. |
| **Package-feed credentials** | A clean restore from every feed the build uses succeeds, private feeds included. A cached restore is not proof. | Re-restore into a fresh package cache in scratch (e.g. `NUGET_PACKAGES`, `npm_config_cache`). Never delete the operator's own cache. Missing or expired credentials can't be healed: raise. |

## Stage B — stack

**When:** at the end of research (Phase 1 §1), before grilling. Re-run the browser check if grilling adds a surface to the E2E scope.

| Check | Probe / pass condition | Heal allow-list |
|---|---|---|
| **Docker** (if anything in the run uses containers) | The daemon is reachable (`docker info`) and the images the build/tests/E2E need are present or can be pulled. | Start or reach the daemon; pull the images. |
| **Local DB schema currency** | Any local database the tests or E2E use is migrated to the base commit's current schema. A stale local schema produces failures that look like defects. | Apply the base commit's migrations to the local database, using the repository's own migration tooling. |
| **E2E host build** | Whatever host the E2E will run (web app, API host, CLI tool, public-API harness) builds from the base commit and starts. | Free the port if a swarm process holds it, otherwise choose another port; clean rebuild in the swarm worktree. |
| **Browser tooling** | The browser tooling the E2E plans will use is enabled and permitted for the app's origin (site approvals, extension connected, or Playwright launching the pre-installed Chromium). | Fall back to Playwright driving the pre-installed Chromium. Site approvals can't be healed: raise. |

## Stage C — sizing

**When:** after the implementation DAG, before the first dispatch. Re-run before every later fan-out (implementation rounds, test runs, reviewers, E2E).

| Check | Probe / pass condition | Heal allow-list |
|---|---|---|
| **Disk** | Free disk ≥ (units about to run × measured per-unit disk footprint) + E2E evidence. | Remove build outputs of accepted or superseded worktrees, and stale evidence dirs. |
| **Memory → concurrency cap** | Compute the cap per the concurrency policy in SKILL.md. Pass if the cap is at least 1. | Lower the cap: the cap *is* the heal. Queue the units that don't fit. |

The per-unit footprint is measured, never guessed: during the baseline (Phase 1 §1 step 6), the orchestrator records one build's peak memory and disk use, and the baseline test executor reports one test run's.

## Recording

The results go into the **Environment block**, which every brief carries:

```text
ENVIRONMENT
Base:                  <branch> @ <SHA>
Env vars / config:     <e.g. TERM=xterm; build configuration>
Pre-existing failures: <test ids, or none>
Footprint (one unit):  disk <measured>, peak memory <measured>
Stage A: disk/memory <pass|healed: …|raised>; base <…>; toolchain <…>; feeds <…>
Stage B: docker <…|n/a>; local DB <…|n/a>; E2E host <…>; browser <…|n/a>
Stage C: cap <N> (free memory <…>, free disk <…>), at <time or round>
```

Stages A and B assemble the block. Stage C adds its line, and replaces it each time it is re-run. Briefs carry the latest version.
