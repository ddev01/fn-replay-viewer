import { defineConfig, devices } from "@playwright/test";

const port = 4321;

export default defineConfig({
  testDir: "tests/ui",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  reporter: "list",
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    colorScheme: "dark",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "node tests/ui/serve.mjs",
    url: `http://127.0.0.1:${port}/index.html`,
    env: { PORT: String(port) },
    reuseExistingServer: false,
    stdout: "ignore",
    stderr: "pipe",
  },
});
