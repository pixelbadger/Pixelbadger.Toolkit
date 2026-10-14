# E2E Design

## 1. Choose the surface for each criterion

Every user-facing acceptance criterion is exercised through exactly one primary surface, recorded in the locked spec's **E2E scope**:

| What the criterion is about | Surface | Executor tooling |
|---|---|---|
| Anything a person sees or does in a web UI: rendering, navigation, forms, client-side behaviour, UI error states | **Web UI** | Chrome browser automation (see below) |
| An HTTP/RPC API contract: status codes, payloads, headers, persistence visible through the API | **API** | `curl` (or the repo's own client) against the running app |
| A command-line tool's behaviour: output, exit codes, files written | **CLI** | Invoke the built binary/tool exactly as a user would |
| A library with no runnable surface | **Public API harness** | A throwaway program in scratch space that consumes the built package, never the internals |

Scoping rules:

- Use the **lowest surface that still proves the criterion**. A criterion about stored data or response shape can be proven through the API or CLI even if the feature also has a UI. A criterion about what the user sees or does in the browser **must** use the web UI. Do not substitute an API call for a UI check.
- A feature that has a web UI gets at least one web-UI happy-path scenario, even when most of its criteria are scoped to API/CLI.
- The system under test is never mocked. External third-party dependencies are real, sandboxed, or stubbed only as the locked spec allows. Record which.
- If a surface's tooling is unavailable in this environment, that is a Zero-Ambiguity Gate failure, not a reason to scope down silently. Raise it with the operator before SPEC LOCK.
- If the environment later prevents the evidence the locked spec names (e.g. a Release build that needs a licence only CI has), propose the closest substitute and get the operator's acknowledgement before relying on it. Record the substitution in the Completion Report.

### Chrome tooling for web UI

Before writing a web-UI plan, check which Chrome tooling this session actually has, and name it in the plan:

1. **Chrome browser MCP tools** (e.g. Claude in Chrome `mcp__claude-in-chrome__*`, or the built-in browser), when available. The executor loads them per their skill guidance.
2. **Playwright driving Chromium**, when no browser MCP is available (e.g. headless cloud containers; use the pre-installed Chromium, never download browsers). The executor writes its Playwright script in its scratch directory, never in the repository.

**Browser economy.** Screenshots are expensive in context and weak as evidence of text. Plans prefer, in order: page text, element finds (selector or accessible name, with the element's text, value or state), and network reads (request/response status and body). A step takes a screenshot only when **layout is what's being judged**: placement, visibility, styling, overlap, rendering. Mark each such scenario `judges layout: yes` in the plan. Those captures stay in the evidence dir.

The web-UI handover carries images on a fixed budget (§4): **one** PNG of the final state the plan names, plus one PNG per identified UI defect. Nothing else is handed over as an image.

Every screenshot is saved as a PNG at the path the plan names. If the chosen browser tool cannot write screenshots to disk, the plan uses Playwright/Chromium for the capture step against the same running app.

## 2. Write the E2E test plan

The plan is the executor's whole world. The mechanical tier follows it literally. Ambiguity in the plan becomes ambiguity in the evidence. Write it so that an executor with no other context can run it and could not reasonably come out with a different verdict.

Finalise the plan against the assembled candidate. Read the actual routes, selectors, commands, ports, and seed data from the code, not from the spec.

Plan rules:

- Every step has one action and one exact expected outcome. "Check it works" is not a step.
- Include negative/error-path scenarios wherever the spec defines failure behaviour.
- Specify waits explicitly (what to wait for, maximum time). Never leave timing to the executor's judgement.
- Group scenarios that share setup into one plan. Split independent plans so they can run in parallel, within the concurrency cap.

## 3. Prepare and dispatch executors

1. **Build once.** The orchestrator builds the candidate before fan-out, so parallel executors do not race on build output. Executors run the prebuilt artefact.
2. **Isolate.** Each parallel executor gets its own port, data directory, and evidence directory. If the app cannot run as multiple instances, run its plans sequentially. How many executors run at once is set by the concurrency cap in SKILL.md (**Concurrency policy**). **Timing and performance scenarios run alone**, with no other unit running. Put them in their own plan and record the machine's core count and load average with the results.
3. **Dispatch** each fixed plan to a **mechanical-tier** subagent with the executor brief below and the plan verbatim. If a scenario that proves an acceptance criterion seems to need judgement, the plan is not strict enough, so rewrite it. Do not escalate it to the judgement tier.
4. **Exploratory sessions** go to the **judgement tier** (see §3a). Use them when you need to probe beyond the scripted paths: edge cases, unexpected input, UI states, or interactions between features.

## 3a. Exploratory sessions

Exploratory sessions find defects. Fixed plans prove criteria. An exploratory session's verdict never counts as acceptance evidence. A finding worth keeping becomes a fixed scenario, run by the mechanical tier, before it counts.

Run exploratory sessions after the fixed plans pass, or alongside them on a separate instance.

**Blind fixtures.** When the mission is to infer hidden properties (e.g. "can an attacker tell the length or the relationships?"), the orchestrator, or a separate setup executor, prepares the target fixtures in scratch space. Fixtures are scratch files, not mutation. The ground truth (plaintexts, keys, derivations) is kept outside the executor's reach and forbidden to it. Score the executor's guesses against the ground truth afterwards. "Could not do better than chance" is valid and useful evidence.

The orchestrator verifies each finding's evidence, as in §5. Confirmed findings either go to the defect ledger, or are turned into fixed scenarios so that a fix can be proven.

## 5. Orchestrator verification

Do not accept PASS as a verdict. Verify it:

1. For every scenario, compare the confirmation evidence with the plan's expected outcome yourself. Open the handover PNG and each UI defect PNG (only those, never per-step captures), and read element text, network reads, API bodies and CLI output.
2. Confirm that the evidence is from the right candidate SHA, that the worktree stayed clean, and that no deviations undermine the result.
3. Map each verified scenario to its acceptance criteria in the traceability record.
4. Open full logs/bodies in the evidence dir only when the handover excerpt is insufficient to judge.

Outcomes:

| Observation | Classification | Action |
|---|---|---|
| Evidence proves the expected outcome | — | criterion evidenced |
| FAIL, and the plan matches the spec | implementation defect | defect ledger → implementation round → re-run the affected plans on the new candidate |
| FAIL or PASS, but the plan, the expectation, or the evidence was wrong or too weak | verification defect | fix the plan and re-dispatch. Never relax an expectation the spec requires |
| BLOCKED after one retry | environment issue | diagnose read-only. If it can't be resolved, record it as a residual risk and tell the operator. A criterion with no E2E evidence is not complete |
| Evidence reveals behaviour the spec never decided | specification defect | return to Phase 1 |

After any implementation round, re-run every E2E plan that touches changed behaviour, on the new candidate SHA. Evidence from an earlier SHA does not count. A re-run may go to the same executor as a message carrying an explicit substitution list (candidate SHA, worktree path, a fresh evidence dir) plus any added scenarios. The executor must not read earlier evidence.
