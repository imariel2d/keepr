---
description: Read CodeRabbit's PR review comments, fix the relevant ones, and reply to every thread.
argument-hint: "[PR number] (defaults to the current branch's PR)"
allowed-tools: Bash(gh:*), Bash(git:*), Bash(dotnet:*), Bash(npx:*), Bash(node:*), Read, Edit, Write, Grep, Glob
---

You are working through CodeRabbit's review on a pull request. Address each of its inline comments,
then reply on every thread. Follow this exactly.

## 1. Find the PR

- If `$ARGUMENTS` contains a number, that's the PR. Otherwise resolve the current branch's PR with
  `gh pr view --json number,headRefName -q .number`.
- Make sure you're on that PR's branch (`git branch --show-current`); if not, check it out first.
  All edits and the commit must land on the PR branch, never on `main`.

## 2. Fetch the review comments

Pull CodeRabbit's **active, top-level** inline comments (skip replies and outdated ones). `gh` fills
in `{owner}/{repo}` from the repo automatically:

```bash
gh api --paginate "repos/{owner}/{repo}/pulls/<PR>/comments" \
  --jq '.[] | select(.user.login|test("coderabbit";"i"))
             | select(.in_reply_to_id==null) | select(.line!=null)
             | {id, path, line, body}'
```

Read every comment body in full. A comment whose `.line` is `null` is outdated/auto-resolved — skip
it.

**Skip threads you've already handled.** Fetch the whole comment list once and build the set of
`in_reply_to_id`s (every reply points at its parent). Any top-level comment already in that set has a
reply — don't fix or re-reply to it. This makes the command safe to re-run: a second pass over the
same review posts nothing. If that leaves zero unaddressed comments, say so and stop — there's
nothing to do until CodeRabbit re-reviews (which only happens after new commits are **pushed**).

## 3. Triage and fix each comment

For each comment, **verify it against the current code first** — some may already be fixed on the
branch, and CodeRabbit is sometimes wrong. Then decide:

- **Relevant and worth doing** → make the change. Keep it minimal and in scope, match the
  surrounding code's style, and don't widen the diff with unrelated cleanups. Group comments that
  point at the same root cause into one fix.
- **Already fixed** on the branch → no edit; you'll say so in the reply.
- **Not worth it** — a low-value nit, a style point that fights the codebase's established pattern,
  a suggestion whose cost exceeds its benefit, or something explicitly deferred by design → no edit.

Load the repo's engineering conventions (the `software-engineering-basics` skill) before editing, and
respect any design-doc decisions (e.g. an open-question/`Q-` that deliberately scopes something out).

## 4. Verify

Before claiming anything works: build and test what you touched.
- Backend: `dotnet build src/Api/Api.csproj` and `dotnet test tests/Api.Tests/Api.Tests.csproj`.
- Frontend: `npx ng build --configuration development` (from `src/ClientApp`). `preview_logs` are
  cumulative and include stale HMR errors — the build result is the authority.
Report failures honestly; don't mark a comment fixed if its change didn't build.

## 5. Commit

One focused commit on the PR branch summarizing the fixes. Stage **only** the files you changed for
this pass — leave unrelated working-tree changes alone. Do **not** push (that's the human's call);
end at the commit and say the commit is local and ready to push.

## 6. Reply to every thread

Post one reply per comment. Keep each to plain English, one or two sentences:

- Implemented → what you changed and why, e.g. *"Fixed — `CreateUser` now catches the unique-index
  violation and returns 409 instead of a 500."*
- Already fixed → *"Already fixed earlier on this branch: …"*
- Declined → start with **`Not relevant:`** and give the short reason, e.g. *"Not relevant: matches
  the existing house pattern and the client reads the JSON body regardless."*

Post via the replies endpoint (pass the body as an argument, not through the shell, to avoid quoting
issues — a tiny `node` script calling `execFileSync('gh', [...])` over a `{id: body}` map is the
reliable way):

```bash
gh api "repos/{owner}/{repo}/pulls/<PR>/comments/<COMMENT_ID>/replies" -f body="<reply>"
```

## 7. Report

Summarize for the user: what you implemented, what was already fixed, what you declined and why, and
how many replies you posted. Remind them the commit is local and ask whether to push.
