// Mirrors the server's PasswordPolicy.MinLength. The server stays the sole authority (it revalidates
// on every submit); this single client-side constant keeps the disabled-button logic and the
// requirement hint from drifting between the login, claim, admin-create, and profile screens.
export const MIN_PASSWORD_LENGTH = 12;

/** Code-point-aware length check (so surrogate-pair characters count as one). */
export function meetsMinLength(password: string): boolean {
  return [...password].length >= MIN_PASSWORD_LENGTH;
}

/** The live "at least N characters" requirement row shown next to a password field. */
export function lengthRequirement(password: string): { label: string; met: boolean }[] {
  return [{ label: `At least ${MIN_PASSWORD_LENGTH} characters`, met: meetsMinLength(password) }];
}
