---
name: feature-e2e-design
description: >
  Design discipline for this repo: whenever you design a feature — a new capability, an endpoint, a
  screen, or a meaningful change to an existing flow — you also design its end-to-end test scenarios
  and explain the full expected output before (or alongside) writing the design. Use this whenever
  you write or update a feature design doc under docs/, propose a feature in feature-status.md, or a
  request amounts to "design/build feature X". The load-bearing rule: a feature design is not done
  until its e2e journeys and their expected outputs are written down.
---

# Design the E2E Tests With the Feature

In this repo, **designing a feature includes designing how you'd prove it works end to end** — the
user journeys through the whole stack, and the exact output each journey should produce. Do this as
part of the design, not after the code. A design that says what to build but not how to verify it is
half a design.

Apply this whenever you:

- write or update a feature design doc (`docs/feature-<N>-<slug>.md` — see
  [docs-naming](../docs-naming/SKILL.md)),
- add or change a row in `docs/feature-status.md`,
- or take on a request that amounts to "design feature X" / "build feature X".

## What "e2e" means here

There is no browser e2e framework in this repo. "End-to-end" means **the whole journey across the
real boundaries the feature touches** — Angular client → API → database / MinIO / email provider →
back to what the user sees. Express each scenario at the highest boundary the feature actually
crosses:

- A backend feature → an **xUnit integration test** in `tests/Api.Tests` that drives the real
  endpoint and asserts the persisted + returned result.
- A UI feature → the **click-path** a person (or the Browser-pane verification workflow) follows,
  plus what renders at each step.
- A full-stack feature → both, named as one journey ("admin invites a user → invitee claims →
  signs in").

Design the scenarios; don't necessarily implement all of them now. Some become automated tests,
some become the manual verification steps you'll run. Either way they're written down.

## The two deliverables, every time

For each feature, before it's "designed", produce:

### 1. The e2e scenarios

Cover, at minimum:

- **The happy path** — the primary journey start to finish.
- **The edge / error paths** — empty input, unauthorized actor, not-found, conflict/duplicate,
  expired or already-used token, provider/transport failure, the boundary values. List the ones
  this feature can actually hit; don't pad.
- **The invariants** — what must stay true regardless (e.g. "the last admin can't be demoted",
  "an invite never leaks an email address").

### 2. The full expected output

For each scenario, spell out **what actually comes back** — not "it works", but the concrete result:

- HTTP status + response body shape (or the ProblemDetails `code`),
- the row(s) written / changed / deleted,
- the side effect (email sent, file moved to MinIO, session revoked) and its observable content,
- what the user sees on screen (the copy, the state change, the redirect).

"Explain the full output" is literal: a reader should be able to run the scenario and check the
result against your description line by line, with nothing left to "and then it should be fine".

## Where it lives

Put both deliverables **in the feature's design doc**, in a `## Testing` (or `## E2E scenarios`)
section, so the scenarios sit next to the design they verify and don't drift from it. Reference them
from `feature-status.md` if that feature's row tracks test status.

When you later implement, the automated slice lands in `tests/Api.Tests` (see
[software-engineering-basics](../software-engineering-basics/SKILL.md) for the test + build + docs
discipline), and the manual slice becomes the verification steps you actually run and report.

## Reporting

When you present a feature design, **walk the reader through the e2e scenarios and their expected
output** — don't bury them. The point of designing them up front is that everyone can see, before a
line of code exists, exactly what "done" will look like. If you later run them, report the real
result honestly (per software-engineering-basics): which scenarios you exercised, which you didn't,
and what the output actually was.
