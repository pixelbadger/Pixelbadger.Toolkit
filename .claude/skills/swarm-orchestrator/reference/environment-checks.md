# Environment Checks

Before spec lock, prove the environment can carry the run to the Completion Gate. A run that discovers mid-integration that Docker is missing or the disk is full has wasted every stream before it.

Check each item, record the result, and fix or raise every gap before SPEC LOCK:

| Check | Pass condition |
|---|---|
| **Free disk** | Enough for one worktree per planned parallel stream, plus their build outputs, plus E2E evidence. Measure one worktree's build footprint; don't guess it. |
| **Docker** | The daemon is reachable (`docker info`) and the images the build/tests/E2E need can be pulled or are present, if anything in the run uses containers. |
| **Package-feed credentials** | A clean restore from every feed the build uses succeeds, private feeds included. A cached restore is not proof. |
| **Browser permission** | The browser tooling the E2E plans will use is enabled and permitted for the app's origin (site approvals, extension connected, or Playwright launching the pre-installed Chromium). |
| **Local DB schema currency** | Any local database the tests or E2E use is migrated to the base commit's current schema. A stale local schema produces failures that look like defects. |
| **E2E host build** | Whatever host the E2E will run (web app, API host, CLI tool, public-API harness) builds from the base commit and starts. |

Results go into the **Environment block** (Phase 1, step 6). Anything that can't be fixed here is a Zero-Ambiguity Gate question for the operator, not something to discover in Phase 4.
