# Implementor Brief

Every implementation stream receives a brief containing the following.

## Context

- locked specification relevant to this stream;
- base branch/commit;
- upstream stream outputs it may rely upon;
- exact scope boundaries;
- the **Environment block** from the baseline: required environment variables, build configuration, and known pre-existing failures.

## Objective

One clear result the worker owns.

## Contract

- interfaces/behaviour it must preserve or create;
- data and state-transition semantics;
- explicit non-goals;
- files/components it owns;
- components it must not redesign.

## Required verification

Name the focused tests/checks that establish correctness. Implementors run **filtered** test runs scoped to their change. The full suite is run separately by the orchestrator's test executors.

Require new tests where behaviour changes.

## Behaviour on uncertainty

The worker must **stop and report** rather than invent a product decision if:

- the locked spec is contradictory;
- a required external behaviour differs from the contract;
- the work requires changing another stream's public contract;
- correctness cannot be established with available evidence.

The worker may make small local engineering choices that do not alter the contract.

## Handover

The worker must:

1. run its focused verification;
2. commit its work;
3. report:
   - commit SHA;
   - files changed;
   - tests/checks run and their results;
   - any scope extensions;
   - any assumptions;
   - any unverified claims;
   - any suspected cross-stream risk;
4. not merge other streams;
5. not claim the overall feature is complete;
6. leave no background process running past the handover. All work must have finished before the report is sent;
7. keep the report compact (aim for 60 lines or fewer): facts, commands and results. No narration;
8. use the commit attribution trailer exactly as the brief gives it.

### Worktrees (mandatory)

- The brief names the absolute worktree path and branch. The worker works only there, using absolute paths.
- Before writing, the worker verifies `HEAD` equals the specified base SHA (or a descendant the brief allows). If it is stale, or the worktree is missing, the worker stops and reports. It never falls back to another directory, including the operator's checkout.
