// Mirrors the server's PasswordPolicy.MinLength. The server stays the sole authority (it revalidates
// on every submit); this single client-side constant keeps the disabled-button logic and the
// requirement hint from drifting between the login, claim, admin-create, and profile screens.
export const MIN_PASSWORD_LENGTH = 12;
