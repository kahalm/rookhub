import { test as base, expect, Page } from '@playwright/test';
import fs from 'fs';
import path from 'path';

const API_URL = 'http://localhost:8085/api';
const STATE_FILE = path.join(__dirname, '..', '.auth-state.json');

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

async function sleep(ms: number) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

async function fetchWithRetry(url: string, init: RequestInit, retries = 5): Promise<Response> {
  for (let i = 0; i < retries; i++) {
    const res = await fetch(url, init);
    if (res.status === 429) {
      await sleep(6_000 * (i + 1));
      continue;
    }
    return res;
  }
  return fetch(url, init);
}

async function registerViaApi(user: { username: string; email: string; password: string }): Promise<AuthResponse> {
  const res = await fetchWithRetry(`${API_URL}/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(user),
  });
  if (!res.ok) {
    throw new Error(`Register failed: ${res.status} ${await res.text()}`);
  }
  return res.json();
}

function loadSharedAuth(): { auth: AuthResponse; username: string; password: string } {
  return JSON.parse(fs.readFileSync(STATE_FILE, 'utf-8'));
}

async function injectAuth(page: Page, auth: AuthResponse): Promise<void> {
  await page.addInitScript((authData) => {
    localStorage.setItem('rookhub_user', JSON.stringify(authData));
  }, auth);
}

/** Fixture that provides a pre-authenticated page using the shared user from global setup */
export const test = base.extend<{ authedPage: Page; testUser: { username: string; password: string } }>({
  testUser: async ({}, use) => {
    const user = uniqueUser();
    await registerViaApi(user);
    await use({ username: user.username, password: user.password });
  },

  authedPage: async ({ page }, use) => {
    // Use the shared user from global setup (no extra API calls = no rate limiting)
    const { auth } = loadSharedAuth();
    await injectAuth(page, auth);
    await use(page);
  },
});

export { expect, uniqueUser, registerViaApi };
