// Readers for the RFC 7807 problem+json bodies the API returns on error. Centralized here so every
// feature screen parses the same server contract the same way, and a shape change lands in one
// place. See docs/feature-36-account-provisioning.md §12.

/** The user-facing `detail` string, or a fallback when the error carries none. */
export function problemDetail(e: unknown, fallback: string): string {
  const detail = (e as { error?: { detail?: string } })?.error?.detail;
  return typeof detail === 'string' && detail ? detail : fallback;
}

/** The HTTP status of a failed HttpClient call, if present. */
export function problemStatus(e: unknown): number | undefined {
  return (e as { status?: number })?.status;
}

/** A machine-readable `code` extension member, when the server tags a problem with one (e.g.
 *  distinguishing two different 409s). */
export function problemCode(e: unknown): string | undefined {
  const code = (e as { error?: { code?: string } })?.error?.code;
  return typeof code === 'string' && code ? code : undefined;
}

/** The per-field validation map from a 400, keyed by field name; empty when the error isn't one. */
export function validationErrors(e: unknown): Record<string, string[]> {
  const errors = (e as { error?: { errors?: Record<string, string[]> } })?.error?.errors;
  return errors && typeof errors === 'object' ? errors : {};
}
