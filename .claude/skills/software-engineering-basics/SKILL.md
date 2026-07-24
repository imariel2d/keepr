---
name: software-engineering-basics
description: >
  Core engineering discipline for working in this repo — how to change code safely, keep docs in
  step with the architecture, and the rules around git. Use this whenever writing, editing,
  refactoring, or reviewing code; adding a feature or making an architectural change; adding or
  changing an API endpoint; running builds or tests; or doing anything with git (committing,
  branching, pushing). Two load-bearing rules:
  NEVER push to a remote unless the user explicitly tells you to in this session, and update or add
  docs whenever a change alters the architecture. Consult this skill before any git operation and
  before claiming a change works.
---

# Software Engineering Basics

Sensible defaults for changing code in this repository. None of this is exotic — it is the
discipline that keeps changes safe, reviewable, and honest.

## The one hard rule: never push unless explicitly told

**Do not run `git push` (or `gh pr merge`, or anything else that publishes work) unless the user
has told you to in this session.** Committing and branching are local and easily undone; pushing
is outward-facing — it moves work onto a shared remote, can trigger CI, notify others, and is
awkward to reverse. **The user decides when their work leaves their machine, every time.**

Concretely:

- Commit freely once a unit of work is done and verified. Branch freely.
- **Wait for an explicit instruction before pushing** — "push", "push it", "push to main", "open a
  PR". A general "commit this" is *not* permission to push.
- Permission is per-request. One "push it" authorizes that push, not the next one.
- If you believe pushing is the natural next step, *say so and ask* — do not just do it.

This is a standing rule. When in doubt, stop at the commit and hand control back.

## Understand before you change

Read the code you are about to touch, and the code around it, before editing. A change that fits
the existing structure is worth more than a clever one bolted on the side. When a bug is reported,
find the actual cause rather than patching the symptom — reproduce it first if you can.

## Keep changes minimal and in scope

Make the smallest change that solves the problem. Resist widening the diff with unrelated cleanups,
renames, or reformatting — they bury the real change and make review harder. If you spot something
worth fixing outside the current scope, note it separately rather than folding it in.

## Match the surrounding code

Write code that reads like the code already there — its naming, its idioms, its comment density,
its error-handling style. Consistency is a feature; a file where one function looks foreign is
harder to maintain than one that is uniformly imperfect.

## Verify against reality — don't claim it works until you've checked

Prefer evidence over assertion. Before saying a change works:

- Build it, and run the tests. Report failures with the actual output.
- For behavior a user would see, exercise it — run it, hit the endpoint, click through it — rather
  than reasoning that it *should* work.
- When a claim is checkable (a value in a database, a status code, a rendered element), check it
  directly instead of inferring it.

If a step was skipped or something is unverified, say so plainly. "Builds and tests pass; I did not
run it end-to-end" is a fine sentence. A confident "done" that turns out to be wrong costs far more
than the honesty would have.

## Keep docs in step with the architecture

When a change touches the **shape** of the system, update the docs in the same change — or add a
new one. This covers a new feature, a new component or service, a changed data model or migration,
new or altered endpoints, a shift in control flow, a new dependency, or removing something others
rely on. A one-line fix inside an existing function usually needs no doc; anything a future reader
would need a mental model for does.

Why this matters: documentation drifts silently. Nothing fails when a doc goes stale, so it rots
unnoticed until someone trusts it and is misled — a stale doc is worse than no doc. Updating it
while the reasoning is fresh in your head is far cheaper than reconstructing it later, and it is
the moment you actually understand the change well enough to explain it.

In this repo specifically:

- `docs/` holds design docs (`*-design.md`) that record not just *what* was built but *why* — the
  alternatives weighed and rejected. When you make a decision worth that treatment, write one in
  the same style; when you change something an existing doc describes, update that doc.
- `docs/feature-status.md` tracks planned vs. implemented features. Adjust it when a feature's
  status changes, and keep any counts or cross-references consistent.
- Touch the `README.md` when you change how the app is run, configured, or deployed.

Prefer updating an existing doc over adding a parallel one that will diverge from it. The goal is
that the docs and the code tell the same story.

## Document API endpoints (Swagger / OpenAPI)

When you add a new endpoint or change an existing one, update its API documentation in the same
change. In this repo the OpenAPI document is **generated from the code**, not hand-maintained, so
"documenting" an endpoint means annotating the action so the generator picks it up:

- An XML `<summary>` on the action describing what it does, and `<param>` comments on request DTO
  fields where the meaning isn't obvious from the name. `Api.csproj` emits the XML doc file and the
  OpenAPI package lifts these into the spec — see `src/Api/OpenApi/OpenApiExtensions.cs`.
- A `[ProducesResponseType<T>(StatusCodes.Status…)]` for **each** status the endpoint can return —
  the success shape and every error it deliberately produces (400, 401, 404, 409, …). Follow the
  existing controllers for the pattern (e.g. `AuthController`, `MeController`).
- If the change adds or alters something cross-cutting — a new auth scheme, a security requirement,
  a shared response — check whether the document transformers in `OpenApiExtensions.cs` need to
  change too.

Why bother: the spec at `/openapi/v1.json` and the Swagger UI at `/swagger` are what API consumers
and any generated typed clients rely on. An endpoint added without these annotations shows up in
the document undocumented or with the wrong response types — quietly wrong, in the one place people
go to trust the contract. Verify by loading Swagger after the change and confirming the endpoint
reads correctly.

## Git hygiene

- **Branch off the default branch** for new work rather than committing straight to it.
- Keep commits focused; a commit should be one coherent change.
- Write commit messages that explain **why**, not just what — the diff already shows the what.
- Don't skip hooks (`--no-verify`) or force-push unless the user explicitly asks. If a hook fails,
  fix the underlying problem.
- Before deleting or overwriting anything, look at what is there first.

## Report honestly

Say what actually happened. If tests fail, show it. If you were unsure and guessed, name the
assumption. Faithful reporting — including of your own mistakes — is what lets the user trust the
parts you say are done.
