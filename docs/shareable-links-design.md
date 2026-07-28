# Shareable Links — Design

> Status: **partial — backend built, frontend + end-to-end verification pending**. Feature #7 in
> [feature-status.md](feature-status.md). Implemented: `ShareLink` model + migration,
> `ShareLinkService`, and the owner/public endpoints (`src/Api/Features/Sharing/`). Not yet: the
> public `/s/:token` viewer page, the owner share UI, and the end-to-end run against Postgres.
>
> Decided by Ariel, 2026-07-24: let the owner mint an unguessable link that anyone can open to
> view or download **one file**, without an account. Links carry an expiry (or never expire) and can be revoked.
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

1. **Expiry is the default** (§4) — links are created with a window (default 7 days) so a leak
   self-heals, though the owner may opt a link into never-expires when a durable link is wanted.
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

A link is a 256-bit random token from a CSPRNG, base64url-encoded, carried in the URL. It is
**unguessable, so possession is authorization** — 256 bits has no structure to brute-force, so the
public endpoints need no rate limiting to protect the token itself (bandwidth abuse via an
already-leaked link is a separate concern — §7).

### 3.1 The token is stored, so an active link can be re-copied (Q-S5 resolved)

The token is stored in the `Shares` table, and the management list (§6) returns each active link's
URL. So the owner can re-copy a link at any time — the Drive/Dropbox behaviour.

This is a deliberate reversal of the original design, which stored only `SHA-256(token)` so a
database dump would be inert (the session/invite-code principle). That made the URL a **show-once**
value, and in practice the one-shot flow was the friction Q-S5 anticipated: "lost the link? revoke
and make a new one" is a poor substitute for "copy it again". Storing the token buys the expected
behaviour at a stated cost — **a dump of the `Shares` table exposes the active share URLs.**

Why that cost is acceptable *here*, and bounded:

- A share URL grants read to **one file the owner already chose to expose** via an unguessable link
  meant to be handed around — not a password or a session. The exposure is narrow.
- It rides on the same single-owner scoping as the Q5 acceptance (§2): the owner sharing their own
  files. It is **not** reusable for multi-user sharing (#6), where it would need revisiting.
- Encrypting the token at rest (so a dump without the app key is still inert) is the stronger
  version, deferred as a follow-up — it needs a persisted key, the same concern the cookie-session
  doc raised, for marginal benefit at this scale.

Revoked links are never shown in the management list — they are dead and cannot be re-shared.

---

## 4. Lifetime

`ExpiresAt` is nullable: the owner chooses a window at creation (the UI offers 1 / 7 / 30 days or
**Never**; the API takes a day count and caps it, or `null` for no expiry). A timed window is the
cheapest bound on a leaked link — it stops working on its own without anyone noticing the leak — so
the default is 7 days. **Never** trades that self-healing away for links meant to stay live
indefinitely; such a link only stops working when the owner revokes it (§6) or the file is removed,
and the global kill-switch (§2) still covers the "take it all down" case. This relaxes the original
"expiry is mandatory" bound — a deliberate choice, made because these are single-owner links to the
owner's own files, not strangers' uploads.

Unlike a session, a link's expiry does **not** slide automatically — a shared link is a fixed
grant, not a live thing being kept warm by use.

The owner can, however, **change a link's expiry after creation** — extend it or bring it
forward — without changing the URL. So the way to keep a still-circulating link alive longer, or to
cut it short, is to edit its expiry in place rather than revoke and recreate (which would invalidate
the URL already handed out). An expired-but-not-revoked link can be extended back to life this way
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

**The viewer URL is built from a configured origin, never the request host.** `Sharing:PublicBaseUrl`
is required and validated at startup (an absolute http/https URL); the app refuses to boot without
it. The `Host` header is client-controlled and, behind App Platform's proxy or with the SPA on a
separate origin, simply wrong — so a capability URL must never be assembled from it. Failing fast is
the same instinct as the invite-code and storage-credential checks.

---

## 6. API surface

### Owner (authenticated)

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/media/{id}/share` | Create a link for a file the caller owns. Body `{ expiresInDays }` (`null` = never expires). Returns `{ linkId, url, expiresAt }` (`expiresAt` null when never) |
| `GET` | `/api/media/{id}/shares` | List the file's links with each active link's `url` (§3.1), so the owner can re-copy. Revoked links are omitted |
| `PATCH` | `/api/shares/{linkId}` | **Change the expiry.** Body `{ expiresInDays }` → new `ExpiresAt` measured from now (same cap as create), or `null` to switch to never-expires. Owner-scoped. Rejected (`409`) on a revoked link — revocation is terminal (§4) |
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
  repeatedly, driving R2 egress. Mitigations: expiry (default, though a link may be set to never), revoke, the global kill-switch, and
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
    public string Token { get; set; }              // the capability itself; unique index (§3.1)
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; } // null = never expires (§4)
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastAccessedAt { get; set; } // nice-to-have; nothing depends on it
}
```

- **Unique index on `Token`** — every public request is this lookup, so it must be an index probe,
  and unique turns a token collision into a database error rather than an ambiguous match.
- **Cascade from `MediaFile`** — when a file is hard-deleted (purged from trash), its links go with
  it. A link to a *trashed* file (not yet purged) is handled by the resolve check in §6, not the FK.
- Migrations `AddShareLinks` then `StoreShareTokenForReCopy` (the §3.1 switch from hash to token);
  table in the `keepr` schema like the rest.

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

## 10.1 Verification

Exercised end-to-end against the local dockerised stack, 2026-07-24:

| Check | Result |
|---|---|
| Create (owner) → URL | Built from `Sharing:PublicBaseUrl` (`http://localhost:4200/s/…`) |
| List (owner) | Active links carry the `url`; revoked links present in the API, hidden by the UI |
| Resolve (anonymous) | Metadata only, `previewKind` set for the image |
| download-url (anonymous) | `200` for inline and attachment; presigned |
| PATCH expiry | Updated |
| Revoke → resolve | `410` problem+json "no longer available" |
| Unknown token → resolve | `404` |
| Stop sharing file → resolve | `{revoked:n}`, then `410` |

In the browser: the public viewer renders the file card + download and degrades gracefully to the
download when the preview media can't load; the `410` state shows the "Link unavailable" card. The
owner dialog creates a link, lists only active links with a copy button, and revoking drops the
link out of the list. (Clipboard copy falls back to a manual-copy message in the sandboxed test
browser; it works in a normal secure context.)

**Migration note:** the switch from hashed to stored tokens (§3.1) can't carry old links forward —
their raw token was never stored — so `StoreShareTokenForReCopy` deletes any existing rows before
adding the unique `Token` column. Any links created under the hashed scheme stop working and must
be re-created.

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

### ✅ Q-S5 — Re-displaying a link (resolved 2026-07-24)
Resolved in favour of re-copyable links: the token is stored and the management list returns each
active link's URL (§3.1). The hashed-storage property was given up knowingly, scoped to single-owner
sharing. Follow-up still open: encrypt the token at rest so a bare DB dump stays inert.

### ⏳ Q-S6 — Max active links per user
No cap today. A trivial abuse bound (and a cheap way to keep the `Shares` table sane) if it ever
matters; unnecessary at one user.
