import { test, expect, uniqueUser, registerViaApi } from './fixtures/auth.fixture';

test.describe('Auth', () => {
  test('login page shows form', async ({ page }) => {
    await page.goto('/login');
    await expect(page.locator('input[name="username"], input[(ngModel)]')).toBeVisible().catch(() => {});
    // Angular Material wraps inputs – look for mat-form-field
    await expect(page.locator('mat-card')).toBeVisible();
    await expect(page.getByRole('button', { name: /login|anmelden/i })).toBeVisible();
  });

  test('login with valid credentials redirects to dashboard', async ({ page, testUser }) => {
    await page.goto('/login');

    // Fill username
    const inputs = page.locator('input');
    await inputs.first().fill(testUser.username);
    await inputs.nth(1).fill(testUser.password);

    await page.getByRole('button', { name: /login|anmelden/i }).click();
    await page.waitForURL('**/dashboard', { timeout: 10_000 });
    await expect(page).toHaveURL(/\/dashboard/);
  });

  test('login with wrong credentials shows error', async ({ page }) => {
    await page.goto('/login');

    const inputs = page.locator('input');
    await inputs.first().fill('nonexistent_user_xyz');
    await inputs.nth(1).fill('WrongPass1!');

    await page.getByRole('button', { name: /login|anmelden/i }).click();

    // Snackbar error message
    await expect(page.locator('.mat-mdc-snack-bar-container, mat-snack-bar-container, simple-snack-bar')).toBeVisible({ timeout: 10_000 });
  });

  test('register redirects to dashboard', async ({ page }) => {
    const user = uniqueUser();
    await page.goto('/register');

    const inputs = page.locator('input');
    await inputs.nth(0).fill(user.username);
    await inputs.nth(1).fill(user.email);
    await inputs.nth(2).fill(user.password);

    await page.getByRole('button', { name: /register|registrieren/i }).click();
    await page.waitForURL('**/dashboard**', { timeout: 10_000 });
    await expect(page).toHaveURL(/\/dashboard/);
  });

  test('register with existing username shows error', async ({ page, testUser }) => {
    await page.goto('/register');

    const inputs = page.locator('input');
    await inputs.nth(0).fill(testUser.username); // already registered
    await inputs.nth(1).fill('duplicate@test.local');
    await inputs.nth(2).fill('Test1234!');

    await page.getByRole('button', { name: /register|registrieren/i }).click();

    await expect(page.locator('.mat-mdc-snack-bar-container, mat-snack-bar-container, simple-snack-bar')).toBeVisible({ timeout: 10_000 });
  });

  test('unauthorized access to /dashboard redirects to /login', async ({ page }) => {
    await page.goto('/dashboard');
    await page.waitForURL('**/login**', { timeout: 10_000 });
    await expect(page).toHaveURL(/\/login/);
  });

  test('logout redirects to /login', async ({ authedPage }) => {
    await authedPage.goto('/dashboard');
    await authedPage.waitForURL('**/dashboard', { timeout: 10_000 });

    // Open user menu (account_circle button)
    const userMenuBtn = authedPage.locator('button').filter({ has: authedPage.locator('mat-icon:text("account_circle")') });
    await userMenuBtn.click();

    // Click logout
    await authedPage.getByRole('menuitem', { name: /logout|abmelden/i }).click();
    await authedPage.waitForURL('**/login', { timeout: 10_000 });
    await expect(authedPage).toHaveURL(/\/login/);
  });

  test('navbar shows username after login', async ({ authedPage, testUser }) => {
    await authedPage.goto('/dashboard');
    await authedPage.waitForURL('**/dashboard', { timeout: 10_000 });

    // Welcome message contains username
    await expect(authedPage.locator('h1')).toContainText(testUser.username, { timeout: 10_000 });
  });
});
