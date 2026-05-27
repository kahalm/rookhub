import fs from 'fs';
import path from 'path';

const API_URL = 'http://localhost:8085/api';
const STATE_FILE = path.join(__dirname, '.auth-state.json');

async function sleep(ms: number) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

async function registerWithRetry(user: { username: string; email: string; password: string }, maxRetries = 10) {
  for (let i = 0; i < maxRetries; i++) {
    const res = await fetch(`${API_URL}/auth/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(user),
    });
    if (res.ok) return res.json();
    if (res.status === 429) {
      await sleep(6_000);
      continue;
    }
    throw new Error(`Register failed: ${res.status} ${await res.text()}`);
  }
  throw new Error('Rate limited after max retries');
}

export default async function globalSetup() {
  const id = Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
  const user = {
    username: `e2e_shared_${id}`,
    email: `e2e_shared_${id}@test.local`,
    password: 'Test1234!',
  };

  const auth = await registerWithRetry(user);

  fs.writeFileSync(STATE_FILE, JSON.stringify({
    auth,
    username: user.username,
    password: user.password,
  }));
}
