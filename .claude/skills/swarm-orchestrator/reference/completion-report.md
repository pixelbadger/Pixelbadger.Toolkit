# Completion Report Template

## Completion Report

### Specification
A short restatement of the locked goal.

### Delivered
The major behaviours implemented.

### Evidence
Acceptance criterion → validating evidence. For E2E: plan/scenario id, surface and tool, candidate SHA, and the key confirmation (element text and handover screenshot path, API status and response excerpt, CLI exit code and output excerpt).

### Swarm execution
Streams, tiers, and notable routing decisions.

### Integration findings
Important defects discovered between streams and how later implementation rounds resolved them.

### Residual risks
Anything genuinely remaining, including skipped environmental tests, evidence substitutions the operator acknowledged, and pre-existing defects left out of scope.

### Change set
Final branch/PR/commits as appropriate.

### Proposed process changes
Lessons from this run that would improve the swarm process, for a human to review. One item per recurring defect class or lesson:

- **Class / lesson:** what went wrong, or what worked, in one line.
- **Evidence:** the defect IDs, rounds or incidents from this run that show it.
- **Proposed amendment:** the skill file and section it would change (e.g. `SKILL.md` → Phase 4 §1 review dimensions, or `briefs/implementor.md` → Handover), and the change in a sentence.

The orchestrator never applies these itself. Write "none" if the run surfaced nothing new.

Do not claim success while relying on an unresolved assumption.
