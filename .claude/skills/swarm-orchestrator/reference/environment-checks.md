# Environment Checks

Prove the environment can carry the run, each stage where it first matters.

## Protocol

**Detect** → if failing, **one heal** from that check's allow-list → **re-check** → **record**: `pass`, `healed: <action>`, or `raised` (still failing, or no heal allowed). A raised check becomes a Zero-Ambiguity Gate question with evidence and a recommendation.

Heals never mutate or touch the operator's checkout; never delete anything the swarm didn't create (only `swarm/*` worktrees and branches, swarm build outputs, scratch, evidence dirs); and never stop a process the swarm didn't start. Needing more means `raised`.

## Stage A — host (at invocation)

| Check | Pass condition | Heal |
|---|---|---|
| **Disk and memory** | Free disk (worktrees, scratch, evidence) and memory recorded; room for one worktree, build and baseline run | Remove stale `swarm/*` worktrees (`git worktree prune`/`remove`), their outputs, stale evidence |
| **Base commit** | `git rev-parse <base>` resolves and checks out | Fetch the base branch once |
| **Toolchain** | Every SDK and tool at the repository's pinned version (`global.json`, `.nvmrc`, …) | None: raise |
| **Measurement** | A peak-memory method works. Try in order: `/usr/bin/time -v`; cgroup v2 `memory.peak` of a transient scope (`systemd-run --scope`); `/proc` process-tree sampling every 0.5 s, as a wrapper exiting with the command; Windows `PeakWorkingSet64`. Record the first that works as `Measurement:` | The fallback order is the heal. None works: raise; memory footprint stays unknown and the cap is 1 |
| **Package feeds** | A clean restore from every feed, private ones included; a cached restore is no proof | Re-restore into a fresh scratch cache (`NUGET_PACKAGES`, `npm_config_cache`), never deleting the operator's. Missing or expired credentials: raise |

## Stage B — stack (end of research, before the baseline)

Re-run the browser check if grilling adds a surface.

| Check | Pass condition | Heal |
|---|---|---|
| **Docker** (if used) | `docker info` succeeds; needed images present or pullable | Start the daemon; pull images |
| **Local DB** (if used) | Migrated to the base commit's schema; a stale schema fakes defects | Apply migrations with the repository's tooling |
| **E2E host** | Builds from base and starts | Free a swarm-held port or pick another; clean rebuild in the swarm worktree |
| **Browser** (if used) | Tooling enabled and permitted for the app's origin | Fall back to Playwright + pre-installed Chromium. Site approvals: raise |

## Stage C — sizing (after the DAG; before every fan-out)

| Check | Pass condition | Heal |
|---|---|---|
| **Disk** | Free ≥ units × per-unit disk footprint + evidence | Remove outputs of accepted/superseded worktrees and stale evidence |
| **Memory → cap** | Cap per the concurrency policy is ≥ 1 | Lower the cap (that is the heal); queue what doesn't fit |

The footprint is measured, never guessed: at the baseline you record the build's peak memory and disk, and the test executor reports the run's, both with the Stage A method. If either is missing, measure the next build or run rather than rebuilding; until then the cap is 1.

## Environment block

Every brief carries the latest version:

```text
ENVIRONMENT
Base:                  <branch> @ <SHA>   (detached: <source branch> @ <SHA> (detached))
Env vars / config:     <e.g. TERM=xterm; build configuration>
Known constraints:     <missing tools, untestable categories, or none>
Measurement:           <Stage A method, as an exact wrapper command>
Pre-existing failures: <test ids | none | pending baseline>
Footprint (one unit):  disk <…>, peak memory <… | not yet measured>
Stage A: disk/memory <pass|healed: …|raised>; base <…>; toolchain <…>; measurement <…>; feeds <…>
Stage B: docker <…|n/a>; local DB <…|n/a>; E2E host <…>; browser <…|n/a>
Stage C: cap <N> (free memory <…>, free disk <…>), at <round> | not yet run
```

Before the baseline returns, failures read `pending baseline` and Stage C `not yet run`. Each Stage C run replaces its line.
