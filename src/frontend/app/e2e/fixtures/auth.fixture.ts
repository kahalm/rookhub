import { test as base, expect, Page } from '@playwright/test';

const API_URL = 'http://localhost:8085/api';

function uniqueUser() {
  const id = Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
  return {
    username: `e2e_${id}`,
    email: `e2e_${id}@test.local`,
    password: 'Test1234!',
  };
}

export interface AuthResponse {
  token: string;
  username: string;
  userId: number;
  isAdmin: boolean;
}

async function registerViaApi(user: { username: string; email: string; password: string }): Promise<AuthResponse> {
  const res = await fetch(`${API_URL}/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(user),
  });
  if (!res.ok) {
    throw new Error(`Register failed: ${res.status} ${await res.text()}`);
  }
  return res.json();
}

async function injectAuth(page: Page, auth: AuthResponse): Promise<void> {
  await page.addInitScript((authData) => {
    localStorage.setItem('rookhub_user', JSON.stringify(authData));
  }, auth);
}

/** Fixture that provides a pre-authenticated page */
export const test = base.extend<{ authedPage: Page; testUser: { username: string; password: string } }>({
  testUser: async ({}, use) => {
    const user = uniqueUser();
    await registerViaApi(user);
    await use({ username: user.username, password: user.password });
  },

  authedPage: async ({ page, testUser }, use) => {
    // Login via API and inject token
    const res = await fetch(`${API_URL}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: testUser.username, password: testUser.password }),
    });
    if (!res.ok) throw new Error(`Login failed: ${res.status}`);
    const auth: AuthResponse = await res.json();
    await injectAuth(page, auth);
    await use(page);
  },
});

export { expect, uniqueUser, registerViaApi };
