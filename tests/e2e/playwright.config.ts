import { defineConfig, devices } from '@playwright/test';

// The app + API + Postgres + MinIO + Mailpit are provided by the docker compose stack
// (docker-compose.yml + docker-compose.api.yml + docker-compose.e2e.yml), so Playwright does not
// start a webServer of its own. Override the origins with E2E_BASE_URL / E2E_MAILPIT_URL if you
// publish the stack on different ports. See docs/testing-strategy.md (Phase 2).
const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:4200';

export default defineConfig({
  testDir: './tests',
  // Journey A is one ordered narrative that mutates shared server state (it rotates the seeded
  // admin's password and invites a user), so tests must not run in parallel or be reordered.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['list']],
  timeout: 60_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    // Runs first: authenticates the admin once and saves its storage state (auth.setup.ts).
    { name: 'setup', testMatch: /.*\.setup\.ts/ },
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
      dependencies: ['setup'],
      testIgnore: /.*\.setup\.ts/,
    },
  ],
});
