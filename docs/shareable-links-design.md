# Shareable Links — Design

> Status: **partial — backend built, frontend + end-to-end verification pending**. Feature #7 in
> [feature-status.md](feature-status.md). Implemented: `ShareLink` model + migration,
> `ShareLinkService`, and the owner/public endpoints (`src/Api/Features/Sharing/`). Not yet: the
> public `/s/:token` viewer page, the owner share UI, and the end-to-end run against Postgres.
>
> Decided by Ariel, 2026-07-24: let the owner mint an unguessable link that anyone can open to
> view or download **one file**, without an account. Links carry an expiry and can be revoked.
>
> This is distinct from #6 *sharing with specific users* — see §1.

---

## 1. Shareable links are not user-to-user sharing

Two features wear the word "share", and they are different mechanisms with different threat models.
Writing down the line between them keeps this design from quietly growing into the other one.

| | **Shareable link (#7, this doc)** | **Sharing with users (#6)** |
|---|---|---|
| Who gets in | Anyone holding the URL — no account | Named accounts you grant |
| The credential | An unguessable token *is* the capability | The recipient's own login + a permission row |
| Revocation | Kill the link | Remove the grant |
| Identity of viewer | Anonymous | Known |

This doc builds the **capability-URL** model only: possession of the link is the authorization.
That is the same shape as a Dropbox/Drive "anyone with the link" URL, and deliberately *not* a
permission system.

---

## 2. The Q5 decision, and why we are shipping anyway

[my-decisions.md](my-decisions.md) Q5 is explicit and load-bearing:

> users WILL be able to share files with each other. Once sharing ships, malware scanning *and*
> content moderation (CSAM/illegal content) become important — legal exposure, not just security.
> Revisit **before** the sharing feature launches.

A public link is the sharpest version of that trigger: a URL can be posted anywhere. So this section
is not a footnote — it is the reason the feature is shaped the way it is.

**The decision: ship, with the risk accepted explicitly and bounded.** The reasoning is specific to
what Keepr actually is today, and does not generalize to #6:

- Keepr is a **private, invite-only, single-owner** deployment. The only person who can upload is
  the owner, and the only person who can create a link is that same owner sharing **their own**
  self-uploaded files.
- The legal exposure Q5 names — redistributing illegal content, distributing malware — is a
  property of **untrusted user-generated content at scale**. With one trusted owner sharing files
  they put there themselves, the risk is closer to "a person emailing their own file" than to
  "a platform hosting strangers' uploads".

This is a genuine acceptance of residual risk, not a claim of zero risk. So it comes with bounds:

1. **Expiry is mandatory** (§4) — no link lives forever, so a leak self-heals.
2. **Revocation** (§6) — the owner can kill any link immediately.
3. **A global kill-switch** — a single config flag disables *all* public link resolution without a
   deploy, for the "take it all down now" case.
4. **Bytes are served from R2's domain, never ours** (§5) — a shared HTML/SVG file cannot execute
   as our origin, and inline rendering still passes `PreviewPolicy`.

**What this does *not* clear:** scanning and moderation remain a hard prerequisite for **#6**, where
other people's uploads enter the picture. This decision is scoped to single-owner link sharing and
must be revisited — not reused — when multi-user sharing is designed. That note belongs back in Q5.

---

## 3. The token is the capability

A link is a 256-bit random token from a CSPRNG, base64url-encoded, carried in the URL. The database
stores only `SHA-256(token)`.

This reuses the session token design wholesale ([cookie-session-design.md](cookie-session-design.md)
§3), for the same reasons:

- **Unguessable, so possession is authorization.** 256 bits has no structure to brute-force, so the
  public endpoints need no rate limiting to protect the token itself (bandwidth abuse via an
  already-leaked link is a separate concern — §7).
- **Stored hashed, so a database dump is inert.** A leak of the `Shares` table must not hand over
  working links to private files. The lookup is an indexed equality probe on the hash, so no
  timing-safe compare is needed on top.

### 3.1 Consequence: the URL is shown once

Because only the digest is stored, the full link cannot be reconstructed later. It is returned
**once**, at creation, for the owner to copy — the same contract as a GitHub personal access token.
The management list (§6) shows a link's existence, expiry, and status so it can be revoked, but not
its URL.

The alternative — storing the token so the link can be re-displayed — is what Drive/Dropbox do, and
is friendlier. It was rejected to stay consistent with the repo's standing principle that a
replayable secret is never stored in plaintext (sessions, the invite code). "Lost the link? Revoke
it and make a new one" is the recovery path. If that proves annoying in practice, revisit as Q-S5.

---

## 4. Lifetime

`ExpiresAt` is **required** — there is no "never expires" option. The owner chooses a window at
creation (the UI offers 1 / 7 / 30 days; the API takes a day count and caps it). Mandatory expiry is
the cheapest bound on a leaked link: it stops working on its own without anyone noticing the leak.

Unlike a session, a link's expiry does **not** slide automatically — a shared link is a fixed
grant, not a live thing being kept warm by use.

The owner can, however, **change a link's expiry after creation** — extend it or bring it
forward — without touching the URL. This pairs directly with the show-once rule (§3.1): because the
token can't be re-displayed, "just make a new link" would mean losing the URL already handed out. So
the way to keep a still-circulating link alive longer, or to cut it short, is to edit its expiry in
place rather than recreate it. An expired-but-not-revoked link can be extended back to life this way
(the URL is still out there); a **revoked** link is terminal and cannot be re-extended — resharing
means a new link. The only thing that ever changes an expiry is an explicit owner action.

---

## 5. Serving model: public page → presigned R2 URL

The link opens `/(s)/{token}` — an **unauthenticated** SPA page — which calls a public API that
validates the token and returns a short-TTL presigned R2 URL. The page renders a preview for
previewable types and a download button for the rest.

Why a page rather than redirecting the link straight to a presigned URL:

- It is a branded surface that can show the filename, size, and an expired/revoked message instead
  of a raw storage error.
- It reuses the existing preview shell and `PreviewPolicy` for inline rendering.
- The raw storage URL is never the thing the user holds or bookmarks.

Why not proxy the bytes through the API: every shared byte would flow through the app server instead
of direct from R2 — the worst option for egress cost and scale. Presigned-direct is how owner
downloads already work; public links keep that property.

**Bytes come off R2's domain.** This is a security property, not just cost: a shared `.html` or
`.svg` opened from a presigned R2 URL cannot run as Keepr's origin, so it cannot touch a session
cookie or the app's DOM. Inline rendering is still gated by `PreviewPolicy` exactly as the owner
preview path is.

---

## 6. API surface

### Owner (authenticated)

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/media/{id}/share` | Create a link for a file the caller owns. Body `{ expiresInDays }`. Returns `{ linkId, url, expiresAt }` — `url` shown once (§3.1) |
| `GET` | `/api/media/{id}/shares` | List the file's links: `linkId`, `createdAt`, `expiresAt`, `revoked`. No URL |
| `PATCH` | `/api/shares/{linkId}` | **Change the expiry.** Body `{ expiresInDays }` → new `ExpiresAt` measured from now, same cap as create. Owner-scoped. Rejected (`409`) on a revoked link — revocation is terminal (§4) |
| `DELETE` | `/api/shares/{linkId}` | **Stop sharing — one link.** Sets `RevokedAt`; idempotent; owner-scoped |
| `DELETE` | `/api/media/{id}/shares` | **Stop sharing the file.** Revokes every live link on the file at once — the "make this file private again" button, without hunting down individual links |

### Public (anonymous)

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/share/{token}` | Resolve to metadata: `fileName`, `contentType`, `sizeBytes`, `previewKind`, `expiresAt`. No owner identity, no internal ids |
| `GET` | `/api/share/{token}/download-url` | Presigned URL, `?disposition=inline\|attachment`, gated by `PreviewPolicy` exactly like the owner path |

The public resolve deliberately returns **only** what the page must render. Not the owner's email,
not the folder, not the file's real id — nothing that widens what a link discloses beyond the one
file it is for.

### Resolve order

A public request is refused unless, in order: the token matches a link, the link is not revoked, the
link is not expired, **and** the underlying file is still live (`Status == Ready`, `DeletedAt ==
null`). A link to a trashed or purged file resolves to "gone", never to the bytes.

Because the token is unguessable, the response can afford to be honest about *why* a link failed
without creating an enumeration oracle: `404` for an unknown token, `410 Gone` for one that is
expired or revoked, so the page can say "this link has expired" rather than a flat "not found".

---

## 7. What a link exposes, and the abuse surface

Honest limits, in the spirit of the registration-gate doc's §7:

- **A valid link is bearer access.** Anyone it is forwarded to can open the file until it expires or
  is revoked. That is the feature, not a flaw — but it means a link is as sensitive as the file.
- **Bandwidth abuse is the real residual risk.** A leaked-but-still-valid link can be fetched
  repeatedly, driving R2 egress. Mitigations: mandatory expiry, revoke, the global kill-switch, and
  — if it becomes a problem — a per-link access cap (Q-S2) or rate limiting on the public endpoints
  (the still-open Q-R1 from the registration-gate doc applies here too, and matters more now that
  there are unauthenticated endpoints).
- **No scanning.** Per §2, accepted for single-owner sharing; a prerequisite for #6.

---

## 8. Data model

```csharp
public class ShareLink
{
    public Guid Id { get; set; }
    public Guid MediaFileId { get; set; }        // cascade: purging the file removes its links
    public MediaFile File { get; set; }
    public Guid CreatedByUserId { get; set; }
    public byte[] TokenHash { get; set; }         // SHA-256(token); unique index
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }  // required
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastAccessedAt { get; set; } // nice-to-have; nothing depends on it
}
```

- **Unique index on `TokenHash`** — every public request is this lookup, so it must be an index
  probe, and unique turns a token collision into a database error rather than an ambiguous match.
- **Cascade from `MediaFile`** — when a file is hard-deleted (purged from trash), its links go with
  it. A link to a *trashed* file (not yet purged) is handled by the resolve check in §6, not the FK.
- Migration `AddShareLinks`; table in the `keepr` schema like the rest.

---

## 9. Interaction with existing features

- **Trash / soft delete.** Trashing a file makes its links resolve to `410` immediately (the
  `DeletedAt` check in §6). Restoring the file brings them back if still unexpired. Purge removes
  them via cascade.
- **Rename.** The link points at the file id; the public page shows the file's *current* name,
  resolved at access time. Renaming does not break a link.
- **Move.** Irrelevant — a link is to a file, not a location.
- **Quota.** Shared reads do not touch quota; the owner was already charged for the bytes. Egress
  cost is the concern, not quota (§7).
- **Sessions.** A link is independent of the owner's session — it keeps working after the owner logs
  out, which is the point. Revocation is the only owner action that ends it.

---

## 10. First public surface

These are the **first unauthenticated, content-serving routes** in the app — every route but
`/login` is behind `authGuard` today. The public viewer page (`/(s)/{token}`) and the
`/api/share/*` endpoints (`[AllowAnonymous]`) are that new surface. It overlaps with the public
layout the legal pages (#32–33) will also need; whoever builds first should factor a shared public
shell so the footer/branding is not duplicated.

---

## 11. Open questions

### ⏳ Q-S1 — Folder links
v1 is single-file only. A folder link means a public recursive browser over a subtree — a whole
read-only file explorer plus subtree access checks. Deferred until single-file links have proven
out.

### ⏳ Q-S2 — Per-link access cap
A "stops after N downloads" option was considered and cut for v1 (extra counter state, fuzzy
definition of a "view"). `LastAccessedAt` is captured so the data exists if we add analytics or a
cap later.

### ⏳ Q-S3 — Password-protected links
Cut for v1. Would add a public password-prompt page and a verify step before any presigned URL is
minted, hashed like the invite code. Straightforward to add on top of this model if wanted.

### ⏳ Q-S4 — Rate limiting the public endpoints (Q-R1)
Unauthenticated endpoints make the still-open Q-R1 more pressing. Token guessing is infeasible, but
bandwidth abuse of a valid link is not. ASP.NET Core's rate limiter keyed by IP, with the
`X-Forwarded-For` handling App Platform needs.

### ⏳ Q-S5 — Re-displaying a link
§3.1 shows the URL once. If "revoke and re-create" proves annoying, the alternative is storing the
token so the list can show it again — at the cost of the hashed-storage property. Left as a
deliberate trade to revisit only if the one-shot flow actually bites.

### ⏳ Q-S6 — Max active links per user
No cap today. A trivial abuse bound (and a cheap way to keep the `Shares` table sane) if it ever
matters; unnecessary at one user.
