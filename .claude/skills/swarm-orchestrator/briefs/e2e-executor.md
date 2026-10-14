# E2E Executor Brief

Executor brief (include verbatim):

```text
You are an E2E executor. Run the plan below exactly as written. You are producing evidence, not fixing anything.

- Do not modify tracked files in any repository or worktree. Write only inside the evidence dir and your scratch dir. Record `git -C <worktree> status --porcelain` before SETUP and again after TEARDOWN; they must match.
- Perform steps in order. Do not skip, reorder, add, or "improve" steps.
- For each step, compare the actual outcome with the expected outcome literally. If they differ, mark the step FAIL, capture failure evidence (a PNG if it is a UI defect, otherwise page text / full response / full output), skip the remaining steps of that scenario, and continue with the next scenario.
- If SETUP fails, or the environment prevents a step from being attempted (tool unavailable, port in use, crash before the step), mark it BLOCKED with the error. You may retry a BLOCKED step once. Never retry a FAIL.
- Do not diagnose root causes, edit code, change configuration, or work around a failure.
- Always run TEARDOWN.
- Return the handover in the exact format specified. Report observations, not conclusions about the feature.
```

## Plan template

Plan template:

```text
E2E PLAN <plan-id>
Candidate:     <branch> @ <SHA>, at <absolute worktree path>
Surface/tool:  <web UI via Claude in Chrome | web UI via Playwright+Chromium | API via curl | CLI>
Evidence dir:  <absolute path in scratch, e.g. <scratch>/e2e/<plan-id>/>
Handover image: <web UI only: the one state to capture for the handover, e.g. "end of scenario S3", and its PNG path>
Isolation:     port <N>, data dir <path>, any env vars. Unique per executor running in parallel.
Environment:   the Environment block (e.g. TERM=xterm), applied to every command.
Glossary:      every command, helper and check a step may name, defined once, exactly
               (e.g. COUNT f := <command>; DECRYPT f := <command>). Steps use only these names.

SETUP (exact commands, in order, each with its expected success signal)
  S1 `<command>`  → expect: <e.g. "listening on :5123" within 30s>
  ...

SCENARIOS
  <scenario-id>: <acceptance criterion id(s) proven>   judges layout: yes|no
    1. <action: exact URL / selector or accessible name / command / request>
       expect: <exact observable outcome: text, status code, exit code, file content>
    2. ... (capture runtime values as named variables, e.g. "record the returned id as $ID")
    FINAL CONFIRMATION: <exactly what to capture, see §4>

TEARDOWN (exact commands; always run, even after failure)
  T1 `<command>`
```

## 4. Executor handover

The executor returns a compact, structured report. Bulk material stays on disk in the evidence dir.

```text
E2E RESULT <plan-id> — candidate <SHA> — tool <tool used>
Worktree clean before/after: yes|no (<diff if no>)
SETUP: OK | BLOCKED (<step, error>)

<scenario-id>: PASS | FAIL | BLOCKED
  steps: 1 PASS, 2 PASS, 3 FAIL
  failing step: <n>: expected <…>; actual <…>
  FINAL CONFIRMATION:
    <evidence, per surface rules below>

TEARDOWN: OK | errors
Deviations from plan: none | <list>
```

Final confirmation evidence per surface:

- **Web UI:** the page URL, the visible text or state of the key element(s) the expectation refers to, and any network response the expectation names. Per handover (not per scenario), the absolute path to **one** PNG of the plan's handover state, plus one PNG for each identified UI defect (a failing step or exploratory finding about what the user sees), taken at the point of failure. No other images.
- **API:** the exact request line, status code, relevant headers, and the response body verbatim (pretty-printed, at most ~60 lines, full body saved to the evidence dir with its path).
- **CLI:** the exact command, exit code, and stdout/stderr verbatim (at most the last ~40 lines each, full output saved to the evidence dir with its path), plus the contents or hash of any file the step expected to be written.
