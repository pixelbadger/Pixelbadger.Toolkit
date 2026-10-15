# E2E Design

## 1. Choose a surface per criterion

Each user-facing criterion gets one primary surface, recorded in the spec's E2E scope:

| Criterion is about | Surface | Tooling |
|---|---|---|
| What a person sees or does in a web UI | **Web UI** | Browser automation (below) |
| An HTTP/RPC contract: status, payload, headers, persistence via the API | **API** | `curl` or the repo's client |
| A CLI's output, exit codes, files written | **CLI** | The built tool, invoked as a user would |
| A library with no runnable surface | **Public API harness** | A scratch program consuming the built package, never internals |

- Use the **lowest surface that proves the criterion**: stored data or response shape can go through API or CLI even when a UI exists, but anything the user sees or does in the browser **must** use the web UI.
- A feature with a web UI gets at least one web-UI happy path.
- Never mock the system under test. Third parties are real, sandboxed, or stubbed only as the spec allows; record which.
- Missing tooling for a surface fails the Zero-Ambiguity Gate: raise it before lock, never scope down silently.
- If the environment later blocks the evidence the spec names (e.g. a licence only CI has), propose the closest substitute, get the operator's acknowledgement, and record it in the report.

**Browser tooling.** Name in the plan what this session has: browser MCP tools (e.g. `mcp__claude-in-chrome__*` or the built-in browser), else Playwright driving the pre-installed Chromium (never download browsers; scripts live in the executor's scratch).

**Browser economy.** Prefer page text, then element finds (with text, value or state), then network reads. Screenshot only when **layout is judged** (placement, visibility, styling, overlap, rendering), marking that scenario `judges layout: yes`. Handover images are capped at one PNG of the plan's named final state plus one per UI defect. If the browser tool can't save PNGs to disk, the plan captures with Playwright against the same app.

## 2. Write the plan

The plan is the executor's whole world, followed literally. Use the format in `briefs/e2e-executor.md`; an executor with no other context must not be able to reach a different verdict. Take routes, selectors, commands, ports and seed data from the candidate's code.

- One action and one exact expected outcome per step. "Check it works" is not a step.
- Include error paths wherever the spec defines failure behaviour.
- State every wait explicitly (what, and for how long); never leave timing to the executor.
- Group scenarios sharing setup; split independent plans so they can run in parallel within the cap.

## 3. Dispatch

1. **Build once** before fan-out; executors run the prebuilt artefact.
2. **Isolate** each parallel executor: own port, data dir, evidence dir. Run plans sequentially if the app can't run twice. **Timing scenarios get their own plan and run alone**, recording core count and load average.
3. **Fixed plans** go to the mechanical tier with `briefs/e2e-executor.md`, `PLAN` filled verbatim. A criterion-proving scenario that seems to need judgement means the plan isn't strict enough: rewrite it rather than escalate.
4. **Exploration** goes to the judgement tier with `briefs/e2e-explorer.md`, for edge cases, odd input, UI states and feature interactions.

## 3a. Exploration

Exploration finds defects; fixed plans prove criteria. Exploratory verdicts never count as acceptance evidence: a finding worth keeping becomes a fixed scenario first. Run charters after fixed plans pass, or alongside on a separate instance, written in the format in `briefs/e2e-explorer.md`.

**Blind fixtures.** When the mission is inferring hidden properties (e.g. "can an attacker tell the length?"), you or a setup executor prepare the fixtures in scratch, keep the ground truth (plaintexts, keys) out of the explorer's reach, and score its guesses afterwards. "No better than chance" is valid evidence.

Verify findings as in §4; confirmed ones go to the ledger or become fixed scenarios that can prove a fix.

## 4. Verify

PASS is not a verdict:

1. Compare each scenario's confirmation evidence with its expected outcome yourself. Open only the handover PNG and defect PNGs; read element text, network reads, bodies and output.
2. Confirm the SHA, the clean worktree, and that no deviation undermines the result.
3. Map each verified scenario to its criteria.
4. Open full logs only if needed.

| Observation | Class | Action |
|---|---|---|
| Evidence proves the outcome | — | Criterion evidenced |
| FAIL; plan matches spec | Implementation | Ledger → fix round → re-run affected plans |
| Plan, expectation or evidence wrong or weak | Verification | Fix the plan and re-dispatch; never relax what the spec requires |
| BLOCKED after one retry | Environment | Diagnose read-only; if unresolved, residual risk, tell the operator. No E2E evidence, not complete |
| Behaviour the spec never decided | Specification | Back to Phase 1 |

After every implementation round, re-run each plan touching changed behaviour on the new SHA; older evidence doesn't count. A re-run may go to the same executor as a substitution list (SHA, worktree, fresh evidence dir) plus any new scenarios; it must not read earlier evidence.
