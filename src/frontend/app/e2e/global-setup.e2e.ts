/**
 * Global setup for E2E test stack (compose.e2e.yml).
 *
 * 1. Waits for API /health (max 120s)
 * 2. Admin-Login → JWT
 * 3. Puzzle-CSV Upload via POST /api/admin/puzzles/import
 * 4. Shared Test-User registrieren → .auth-state.json
 */
import fs from 'fs';
import path from 'path';

const API_URL = process.env.E2E_API_URL || 'http://localhost:5002';
const STATE_FILE = path.join(__dirname, '.auth-state.json');

const ADMIN_USERNAME = process.env.E2E_ADMIN_USERNAME || 'e2e_admin';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD || 'E2eAdmin123!';

async function sleep(ms: number) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

async function waitForApi(maxWaitMs = 120_000) {
  const start = Date.now();
  while (Date.now() - start < maxWaitMs) {
    try {
      const res = await fetch(`${API_URL}/health`);
      if (res.ok) return;
    } catch {
      // not ready yet
    }
    await sleep(2_000);
  }
  throw new Error(`API did not become healthy within ${maxWaitMs / 1000}s`);
}

async function adminLogin(): Promise<string> {
  const res = await fetch(`${API_URL}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: ADMIN_USERNAME, password: ADMIN_PASSWORD }),
  });
  if (!res.ok) {
    throw new Error(`Admin login failed: ${res.status} ${await res.text()}`);
  }
  const data = await res.json() as { token: string };
  return data.token;
}

async function importPuzzles(token: string) {
  const csvPath = path.join(__dirname, 'fixtures', 'test-puzzles.csv');
  const csvContent = fs.readFileSync(csvPath);

  // Use FormData with Blob (Node 18+ native fetch)
  const formData = new FormData();
  formData.append('file', new Blob([csvContent], { type: 'text/csv' }), 'test-puzzles.csv');

  const res = await fetch(`${API_URL}/api/admin/puzzles/import`, {
    method: 'POST',
    headers: { 'Authorization': `Bearer ${token}` },
    body: formData,
  });
  if (!res.ok) {
    throw new Error(`Puzzle import failed: ${res.status} ${await res.text()}`);
  }
  const data = await res.json() as { imported: number };
  console.log(`  Imported ${data.imported} puzzles`);
}

async function registerSharedUser() {
  const id = Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
  const user = {
    username: `e2e_shared_${id}`,
    email: `e2e_shared_${id}@test.local`,
    password: 'Test1234!',
  };

  const res = await fetch(`${API_URL}/api/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(user),
  });
  if (!res.ok) {
    throw new Error(`Register failed: ${res.status} ${await res.text()}`);
  }
  const auth = await res.json();

  fs.writeFileSync(STATE_FILE, JSON.stringify({
    auth,
    username: user.username,
    password: user.password,
  }));
  console.log(`  Registered shared user: ${user.username}`);
}

export default async function globalSetup() {
  console.log('[E2E Setup] Waiting for API...');
  await waitForApi();
  console.log('[E2E Setup] API is healthy');

  console.log('[E2E Setup] Admin login...');
  const token = await adminLogin();

  console.log('[E2E Setup] Importing test puzzles...');
  await importPuzzles(token);

  console.log('[E2E Setup] Registering shared test user...');
  await registerSharedUser();

  console.log('[E2E Setup] Done');
}
