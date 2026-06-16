# Plan: Job-Seeker Web App — .NET 10 + Angular 20

## Context

The job-seeker Python scraper already runs as an Azure Container App Job. We now need a companion web application to provide a customer-facing UI and REST API. The App Service infrastructure (`webapp.bicep`) is already provisioned; this plan covers creating the actual application code and wiring it into CI/CD.

**Stack:** ASP.NET Core (.NET 10) + Angular 20 SPA, deployed as a zip to the existing `job-seeker-api-{env}` App Service via `azure/webapps-deploy@v3` — the same pattern used in joe-prints.

**Key decisions:**
- User accounts stored in **Azure Table Storage** (`Customers` table) — already provisioned, no extra cost
- **Remember Me** checkbox: checked → localStorage (7-day JWT), unchecked → sessionStorage
- Dashboard shows a **placeholder** only for now ("Welcome, {firstName}!")
- Authenticated user's GUID `id` **is their `customer_id`** in the scraper system
- No MFA; password reset via email link only
- Bootstrap 5.3 via angular.json arrays — **no SCSS**
- Patterns follow joe-prints closely; call out differences where they exist

---

## Files to Create / Modify

| Action | Path |
|--------|------|
| Create | `job-seeker.sln` |
| Create | `job-seeker.Server/` (full .NET 10 project) |
| Create | `job-seeker.client/` (full Angular 20 project) |
| Modify | `bicep-templates/main.bicep` |
| Modify | `bicep-templates/webapp.bicep` |
| Modify | `.github/workflows/deploy.yml` |

---

## 1. Backend — `job-seeker.Server`

### 1a. Project File (`job-seeker.Server.csproj`)
- TFM: `net10.0`, SDK: `Microsoft.NET.Sdk.Web`
- SpaRoot: `..\job-seeker.client`, SpaProxyLaunchCommand: `npm start`, SpaProxyServerUrl: `https://localhost:49152`
- ProjectReference to `job-seeker.client.esproj`

**NuGet packages:**
```xml
Microsoft.AspNetCore.Authentication.JwtBearer     10.*
Azure.Data.Tables                                  12.*
Azure.Identity                                     1.*
Azure.Extensions.AspNetCore.Configuration.Secrets  1.*
Resend                                             0.2.*
Swashbuckle.AspNetCore                             6.*
Microsoft.AspNetCore.SpaProxy                      10.*-*
Microsoft.ApplicationInsights.WorkerService        2.*
```

### 1b. Azure Table Storage Schema

**`Customers` table** (look up by email at login = O(1) point lookup):
| Field | Notes |
|-------|-------|
| PartitionKey | lowercased email |
| RowKey | customerId GUID — **this is the customer_id** |
| Id | same value as RowKey (convenience duplicate) |
| Email | mirrors PartitionKey |
| PasswordHash | PBKDF2-SHA512, base64 |
| PasswordSalt | 16-byte random, base64 |
| FirstName | string |
| LastName | string |
| CreatedDate | ISO 8601 UTC |

No `PasswordResetTokens` table — reset tokens are short-lived JWTs (see §1e below). No extra storage required.

### 1c. Password Hashing
- PBKDF2 + SHA512, 350,000 iterations, 64-byte key, 16-byte salt
- `CryptographicOperations.FixedTimeEquals()` for timing-safe comparison

File: `job-seeker.Server/Operations/Auth/PasswordHasher.cs`

### 1d. JWT Token Handler
- `JsonWebTokenHandler`, HmacSha256
- Session tokens: Subject = customer GUID (= customer_id), expiry from `jwt-expiration-in-minutes` KV secret
- Password-reset tokens: separate method `CreatePasswordResetToken()`, hardcoded 15-min expiry, extra `purpose=password-reset` claim

File: `job-seeker.Server/Authorization/TokenHandler.cs`

### 1e. CustomerController (`/customer/...`)

| Endpoint | Auth | Notes |
|----------|------|-------|
| `POST /customer/sign-up` | none | Validate → check email uniqueness (409 if duplicate) → hash password → create customer → return JWT + sessionStartDate |
| `POST /customer/sign-in` | none | Look up by email → compare hash → return JWT + sessionStartDate. 401 on mismatch |
| `POST /customer/forgot-password` | none | Look up customer → generate 15-min password-reset JWT → send Resend email with link `/reset-password/{jwt}`. Always return 200 |
| `POST /customer/reset-password` | none | Validate JWT (not expired, `purpose=password-reset` claim) → hash new password → update customer |
| `GET /customer/me` | `[Authorize]` | Parse Subject from JWT → return Customer DTO |

**Input validation:**
- Email: required, valid format, max 254 chars
- Password: 8–29 chars, ≥1 uppercase, ≥1 special character
- FirstName / LastName: required, min 3 chars, max 50 chars
- 400 on validation errors; 409 on conflicts

### 1f. Email Service
- Provider: Resend (`resend-api-key` KV secret — shared with container job)
- From address: `resend-from-address` KV secret
- Reset link: `{app-base-url}/reset-password/{jwt}`

Files: `job-seeker.Server/Operations/Email/SendEmail.cs`, `PasswordReset.html`

### 1g. Program.cs
- Key Vault loaded via `KEY_VAULT_URI` app setting + `DefaultAzureCredential`
- Table Storage via `AZURE_TABLE_SERVICE_ENDPOINT` app setting + managed identity (no connection string)
- JWT Bearer auth with KV-sourced signing key

---

## 2. Frontend — `job-seeker.client`

### 2a. Project Setup
- Angular 20, NgModule-based
- Bootstrap 5.3 added via `angular.json` styles/scripts arrays (no SCSS)
- Dev proxy: `/customer/` → ASP.NET Core backend

### 2b. AuthService
- `BehaviorSubject<boolean>` isLoggedIn observable
- `login(token, rememberMe)` → localStorage or sessionStorage
- `logout()` → clears both, emits false
- `getToken()` → checks localStorage then sessionStorage, validates JWT `exp`

### 2c. Components

| Component | Route | Description |
|-----------|-------|-------------|
| `HomeComponent` | `/` | Hero: "Matt's Job Seeker" / "A smarter way to job search". CTA to sign-up or dashboard |
| `AboutComponent` | `/about` | Static about page |
| `SignInComponent` | `/sign-in` | email + password + rememberMe → JWT → dashboard |
| `SignUpComponent` | `/sign-up` | firstName (min 3) + lastName (min 3) + email + password + confirmPassword + rememberMe |
| `ForgotPasswordComponent` | `/forgot-password` | Email form → success toast "Reset link sent! Check your inbox." |
| `ResetPasswordComponent` | `/reset-password/:id` | password + confirmPassword → redirect to sign-in on success |
| `DashboardComponent` | `/dashboard` | Client redirect to sign-in if no token. "Welcome, {firstName}!" placeholder |
| `NavbarComponent` | — | Logged in: Home/About/Dashboard/Log Out. Logged out: Home/About/Login/Sign Up |

### 2d. Routing
No route guards. Dashboard handles client-side auth check internally.

---

## 3. Bicep Changes

### `bicep-templates/main.bicep`
- Add `Customers` table resource under existing `tableService`
- Pass `storageAccount.properties.primaryEndpoints.table` and `keyVault.properties.vaultUri` to `webAppDeploy` module

### `bicep-templates/webapp.bicep`
- Add params `tableServiceEndpoint` and `keyVaultUri`
- Add app settings `AZURE_TABLE_SERVICE_ENDPOINT` and `KEY_VAULT_URI`

### Key Vault Secrets (add manually post-deploy)
| Secret | Value |
|--------|-------|
| `jwt-secret` | 256-bit random base64 |
| `jwt-issuer` | `job-seeker` |
| `jwt-audience` | `job-seeker-api` |
| `jwt-expiration-in-minutes` | `10080` (7 days) |
| `resend-api-key` | reuse existing |
| `resend-from-address` | `noreply@job-seeker.app` |
| `app-base-url` | `https://job-seeker-api-dev.azurewebsites.net` |

---

## 4. CI/CD — `.github/workflows/deploy.yml`

Add `build-and-deploy-webapp` job:
1. Setup .NET 10 + Node 22
2. `npm ci` in `job-seeker.client/`
3. `dotnet restore` + `dotnet build --configuration Release`
4. `dotnet publish` → zip publish folder → `azure/webapps-deploy@v3` to `job-seeker-api-dev`
5. Deploy main.bicep (infra update for new table + app settings)

New GitHub variable needed: `KV_ADMIN_OBJECT_ID`

---

## Verification

1. `dotnet run` locally → SPA proxy serves Angular at same origin as API
2. Sign-up → JWT stored → dashboard "Welcome, {firstName}!"
3. Sign-in → correct credentials → dashboard; wrong → error
4. Forgot password → email received → link → new password → sign-in works
5. Nav toggles correctly between logged-in and logged-out states
6. Remember Me: with = survives browser restart; without = clears on tab close
7. CI/CD push → Actions build succeeds → app live on App Service
8. `Customers` table visible in Azure Portal Storage Account
