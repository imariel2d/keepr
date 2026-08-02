// Shared constants for the e2e suite. Origins are overridable so the same specs run against a
// stack published on non-default ports.

export const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025';

// The bootstrap admin seeded by the dev/e2e stack (docker-compose.api.yml, Admin__Email/Password).
// Its password is an *initial* secret and the account carries must-change, so the suite rotates it
// on first sign-in (see support/admin.ts).
export const ADMIN_EMAIL = 'admin@keepr.local';
export const ADMIN_INITIAL_PASSWORD = 'keepr-dev-admin';
// Rotated-to password. Must satisfy PasswordPolicy: >= 12 chars and must not contain the email's
// local part ("admin"). Random-ish so it isn't in the breach corpus.
export const ADMIN_PASSWORD = 'Ada-Keepr-2026-e2e-pw';

// A first/last name on the admin, so the invite email's inviter line is exercised — the email must
// show this name and never an @-address (User.DisplayName, EmailTemplates.Invite).
export const ADMIN_FIRST = 'Ada';
export const ADMIN_LAST = 'Lovelace';
export const ADMIN_DISPLAY_NAME = `${ADMIN_FIRST} ${ADMIN_LAST}`;

// The user journey A invites and then claims. The password must be >= 12 chars and must not contain
// the local part ("newuser").
export const NEW_USER_EMAIL = 'newuser@example.com';
export const NEW_USER_PASSWORD = 'Fresh-Keepr-2026-pass';
