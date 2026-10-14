# E2E Explorer Brief

Give the Sonnet executor a **charter** instead of a step list:

```text
E2E CHARTER <charter-id>
Candidate, surface/tool, evidence dir, isolation, SETUP, TEARDOWN: as for a fixed plan
Mission:   <what to probe, e.g. "invalid and boundary input on the config form">
Spec refs: <the acceptance criteria and failure semantics that define correct behaviour>
Locked decisions: <deliberate behaviours that are expected, not findings (e.g. "NUL is stripped wherever it appears")>
In scope / out of scope: <areas, actions not allowed (e.g. no destructive admin actions)>
Budget:    <max scenarios or tool calls, max screenshots retained>
```

The executor brief is the same as for fixed plans, with the step-order rules replaced by: "Explore within the mission and budget. When behaviour contradicts the spec refs or looks wrong, record it as a finding, with exact reproduction steps and evidence, then move on. Do not diagnose or fix."

Exploratory handover:

```text
E2E EXPLORATION <charter-id> — candidate <SHA> — tool <tool used>
Worktree clean before/after: yes|no
Coverage: <what was exercised, briefly>
FINDING <n>: <observed vs expected per spec ref>
  repro: <numbered exact steps>
  evidence: <per surface rules in §4; a UI defect includes its PNG>
No findings in: <areas probed that behaved per spec>
```
