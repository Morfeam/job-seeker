# Scrape Integration: Web App → Container App Job

## Context

The job-seeker system has two independent parts: a Python scraper that runs as an Azure Container App Job, and a .NET 10 + Angular 20 web app for customer auth. This feature wires them together — a signed-in user fills out a scrape form on the dashboard, the .NET API validates the request and triggers the Container App Job via the Azure SDK, and a Bootstrap toast shows the result.

This spans four layers: Python scraper (new `--criteria-id` arg), .NET backend (new `JobSeekerController` + `CriteriaTableStorage`), Angular frontend (inline form + toast on Dashboard), and Bicep (new table + role assignment + app settings).

---

## 1. Python Scraper Changes

### 1a. `job-seeker-scraper/main.py`
Add `--criteria-id` arg to `setup_and_parse_args()`:
```python
parser.add_argument("--criteria-id", type=str, default="default",
                    help="Criteria blob ID — determines which criteria file to load (default: 'default')")
```
Update `load_criteria` call to pass `criteria_id`:
```python
criteria = load_criteria(customer_id, args.criteria_id)
```

### 1b. `job-seeker-scraper/storage/blob.py`
Update `load_criteria` signature and blob path:
```python
def load_criteria(customer_id: str, criteria_id: str = "default") -> dict:
    blob_name = f"{customer_id}/{criteria_id}.json"   # was: {customer_id}/criteria.json
    client = _get_blob_service_client().get_blob_client(container=CRITERIA_CONTAINER, blob=blob_name)
    return json.loads(client.download_blob().readall())
```

> **Data migration note:** Existing criteria blobs at `criteria/{customerId}/criteria.json` must be re-uploaded to `criteria/{customerId}/default.json` (or any chosen `criteriaId`), and a corresponding row added to the new `CustomerCriteria` table. One-time manual step per customer in dev.

---

## 2. Backend — `job-seeker.Server`

### 2a. NuGet Package
Add to `job-seeker.Server.csproj`:
```xml
<PackageReference Include="Azure.ResourceManager.AppContainers" Version="1.*" />
```
(`Azure.ResourceManager` is a transitive dependency — no separate listing needed.)

### 2b. New Models

**`Models/ScrapeRequest.cs`**
```csharp
public class ScrapeRequest {
    [Required] public bool ScrapeLinkedin { get; set; }
    [Required] public bool ScrapeIndeed { get; set; }
    public bool ScrapeZiprecruiter { get; set; } = false;
    [Required] public string CriteriaId { get; set; } = string.Empty;
}
```
> The form sends `criteriaId` (not name). `GET /criteria` returns `{id, name}` pairs; the Angular dropdown binds to `id` as the value and shows `name` as the label. This avoids a name→id lookup in the scrape endpoint.

**`Models/CriteriaItem.cs`** — `{ string Id; string Name }`

**`Models/CriteriaResponse.cs`** — `{ List<CriteriaItem> Criteria }`

**`Models/CustomerCriteria.cs`** (internal) — `{ string Id; string Name }`

### 2c. Criteria Storage

**`Operations/Storage/ICriteriaStorage.cs`**
```csharp
public interface ICriteriaStorage {
    Task<List<CustomerCriteria>> GetByCustomerIdAsync(string customerId);
}
```

**`Operations/Storage/CriteriaTableStorage.cs`**
Table: `CustomerCriteria` | PartitionKey = `customerId` | RowKey = `criteriaId` (GUID) | `CriteriaName` column

`GetByCustomerIdAsync` queries `PartitionKey eq '{customerId}'` and maps `RowKey→Id`, `CriteriaName→Name`.
Pattern mirrors `CustomerTableStorage.cs` exactly — `TableServiceClient` injected, `QueryAsync<TableEntity>`.

### 2d. `Controllers/JobSeekerController.cs`

Route: `[Route("job-seeker")]`

**`GET /job-seeker/criteria`** `[Authorize]`
- Extract `customerId` from JWT (same `User.FindFirst` pattern as `CustomerController.GetMe`)
- Call `_criteriaStorage.GetByCustomerIdAsync(customerId)`
- Return `200 CriteriaResponse`

**`POST /job-seeker/scrape`** `[Authorize]`
- Extract `customerId` from JWT
- Validate `ScrapeLinkedin || ScrapeIndeed` → `400` if neither is true
- Build ARM resource identifier:
  ```csharp
  var jobId = ContainerAppJobResource.CreateResourceIdentifier(
      _config["AZURE_SUBSCRIPTION_ID"], _config["AZURE_RESOURCE_GROUP"], _config["CONTAINER_JOB_NAME"]);
  var job = _armClient.GetContainerAppJobResource(jobId);
  ```
- Build `ContainerAppJobExecutionTemplate` with container name `"job-seeker"` and args:
  `--scrape-indeed {true|false} --scrape-linkedin {true|false} --scrape-ziprecruiter false --customer-id {customerId} --criteria-id {criteriaId} --enrich true`
- AI enrichment is always on for web-app-triggered scrapes (`--enrich true` in `BuildArgs`); requires the `ANTHROPIC_API_KEY` env var (from the `anthropic-api-key` Key Vault secret on the container job, which the web app's execution inherits).
- Call `await job.StartAsync(WaitUntil.Started, template)`
- Return `202 Accepted` on success
- `RequestFailedException` Status 404 → `404`; Status 409 → `409 { error = ex.Message }`; other → `409 { error = "Failed to start job." }`

Constructor injects: `IConfiguration`, `ICriteriaStorage`, `ArmClient`

### 2e. `Program.cs` additions
After existing `credential` definition, add:
```csharp
builder.Services.AddSingleton(new ArmClient(credential));
builder.Services.AddSingleton<ICriteriaStorage, CriteriaTableStorage>();
```

### 2f. Configuration

**`appsettings.Development.json`** — add keys (values filled in `local.settings.json`):
```json
"AZURE_SUBSCRIPTION_ID": "",
"AZURE_RESOURCE_GROUP": "",
"CONTAINER_JOB_NAME": "job-seeker-job-dev"
```

**`local.settings.json`** (gitignored) — add real subscription ID and resource group.

**`job-seeker.client/src/proxy.conf.js`** — add `/job-seeker` context:
```js
{ context: ['/job-seeker'], target: 'https://localhost:7107', secure: false }
```

---

## 3. Frontend — Dashboard Component

### 3a. `dashboard.ts` changes

Additions:
- `declare var bootstrap: any;` at file top
- Inject `FormBuilder` in constructor
- `criteria: CriteriaItem[] = []`
- `isScraping = false`
- `toastMessage = ''`
- `@ViewChild('successToast') successToastRef!: ElementRef`
- `@ViewChild('errorToast') errorToastRef!: ElementRef`
- `scrapeForm = this.fb.group({ scrapeIndeed: [false], scrapeLinkedin: [false], criteriaId: ['', Validators.required] })`
- On `ngOnInit`: after auth check, also call `GET /job-seeker/criteria` (same headers pattern) to populate `this.criteria`
- `atLeastOneSourceSelected()` helper — returns `true` if either checkbox is checked
- `onScrape()` — POST `/job-seeker/scrape` with auth header, set `isScraping` flag, show appropriate toast on complete/error

### 3b. `dashboard.html` changes

1. **Toast container** — fixed bottom-right, two toasts (`#successToast` green, `#errorToast` red with `{{ toastMessage }}`)

2. **Scrape form section** — inline Bootstrap form between the hero and the placeholder cards:
   - Criteria `<select>` bound to `criteriaId` (options from `criteria` array, `[value]="c.id"`, display `c.name`)
   - Indeed checkbox, LinkedIn checkbox, ZipRecruiter checkbox (disabled with muted label)
   - Submit button disabled when `scrapeForm.invalid || isScraping || !atLeastOneSourceSelected()`
   - Spinner inside button while `isScraping`
   - Inline validation error when both sources unchecked and form dirty

### 3c. `app-module.ts` — no changes (Dashboard already declared, `ReactiveFormsModule` + `HttpClientModule` already imported)

---

## 4. Bicep Infrastructure

### 4a. `bicep-templates/main.bicep`
Add `CustomerCriteria` table under existing `tableService`:
```bicep
resource customerCriteriaTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: tableService
  name: 'CustomerCriteria'
}
```
Add `managedIdentityPrincipalId` to `main.bicep` outputs and pass it to the `containerEnvDeploy` module call where it's already used. Also expose it for `deploy.yml` to capture as a job output.

### 4b. `bicep-templates/webapp.bicep`
Add three non-secret app settings:
```bicep
{ name: 'AZURE_SUBSCRIPTION_ID', value: subscription().subscriptionId }
{ name: 'AZURE_RESOURCE_GROUP',  value: resourceGroup().name }
{ name: 'CONTAINER_JOB_NAME',    value: 'job-seeker-job-${environment}' }
```

### 4c. `bicep-templates/container-job.bicep`
Add `managedIdentityPrincipalId` param and a `Contributor` role assignment scoped to the container app job resource:
```bicep
param managedIdentityPrincipalId string

var contributorRoleId = 'b24988ac-6180-42a0-ab88-20f7382dd24c'
resource jobTriggerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerAppJob.id, managedIdentityPrincipalId, contributorRoleId)
  scope: containerAppJob
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributorRoleId)
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}
```

### 4d. `.github/workflows/deploy.yml`
- Add `managedIdentityPrincipalId` to the `deploy-infra` job outputs (from the Bicep output)
- Pass `managedIdentityPrincipalId` as a parameter to the `container-job.bicep` deployment step

> **Permission note:** This role assignment requires the CI service principal to have `Owner` or `User Access Administrator` — the same elevation needed for the existing `Storage Table Data Contributor` assignment.

---

## Verification

1. **Local dev**: `dotnet run` → navigate to `/dashboard` signed in → confirm criteria dropdown loads (requires at least one row in `CustomerCriteria` table for the signed-in user's customerId)
2. **Happy path**: Select criteria, check Indeed, click "Start Scrape" → green toast; confirm execution appears in Azure Portal under the Container App Job
3. **Validation**: Submit with no sources checked → inline error, button stays disabled
4. **Error path**: Set `CONTAINER_JOB_NAME` to an invalid name → red toast with error message
5. **Post-deploy**: Trigger from live site → verify execution started, blobs saved under `{customerId}/{criteriaId}/`, `JobLinks` table updated

---

## Addendum A: Displaying scraped jobs

### Context
Once a scrape has run, its results live as JSON blobs in `job-seeker-results/{customerId}/{blobFileName}` with a matching row in the `JobLinks` table (`BlobFileName`, `criteriaId`). This addendum adds a **My Jobs** page so a signed-in user can browse the jobs found for them, grouped and filterable by criteria.

### Backend — `GET /job-seeker/get-jobs` `[Authorize]`
Returns the user's most recent jobs (cap **100**), grouped by criteria.

- **Storage additions**
  - `IJobLinkStorage` / `JobLinkTableStorage` — `GetRecentByCustomerIdAsync(customerId, max)`: queries `JobLinks` by `PartitionKey eq customerId`, sorts by `Timestamp` desc in-memory (Table Storage has no server-side ordering), returns top `max` as `JobLink { JobId, BlobFileName, CriteriaId, Timestamp }`.
  - `IJobResultStorage` / `JobResultBlobStorage` — `GetJobAsync(customerId, blobFileName)`: downloads `job-seeker-results/{customerId}/{blobFileName}`, deserializes into `JobResult` with `JsonNamingPolicy.SnakeCaseLower` (maps the scraper's snake_case JSON). Returns `null` on 404 so orphaned link rows are skipped.
- **Models**: `JobResult` (mirrors `NormalizedJob`), `JobTechnology`, `CriteriaJobsGroup { criteriaId, criteriaName, jobs[] }`, `JobLink`.
- **Controller flow** (`JobSeekerController.GetJobs`): get customerId from JWT → fetch recent links → read each blob in parallel (`Task.WhenAll`) → resolve `criteriaId → name` via `ICriteriaStorage` (fallback `"Uncategorized"` for empty/unknown) → `GroupBy(criteriaId)` → return `List<CriteriaJobsGroup>`.
- **DI / config**: register `BlobServiceClient` in `Program.cs` (`AddBlobServiceClient`) using `AZURE_BLOB_SERVICE_ENDPOINT` + managed identity in Azure, or `storage-connection-string` locally. New `AZURE_BLOB_SERVICE_ENDPOINT` app setting wired through `main.bicep` → `webapp.bicep` from `storageAccount.properties.primaryEndpoints.blob`. Adds `Azure.Storage.Blobs` package.

### Frontend — `UserJobs` component, route `/my-jobs`
- Reached from the dashboard **My Jobs** card (now a `routerLink="/my-jobs"` button).
- On init: auth-guard (redirect `/sign-in` if no token) → `GET /job-seeker/get-jobs`. Shows a **blue spinner** until the response returns; on error **routes to `/error`** (new `ErrorPage` component, route `/error`).
- **Header** matches the About banner style (`py-4 bg-primary text-white`) showing **"My Jobs"** plus a criteria **filter dropdown** (`All criteria` + one option per returned group). Changing the filter resets paging.
- **Paging**: renders 10 job cards; a **"Load more jobs"** button reveals 10 more, up to the 100 returned.
- **Job card** contains: (1) a placeholder black box icon for the job board (`job.source`), (2) title (links to `job_url`), company, location, salary, (3) an **AI Insights** section — `yearsExperience`, `technologies` (badges; preferred highlighted), `hardRequirements`, `softRequirements`, (4) a **collapsible** area (Bootstrap collapse) showing `aiSummary`, (5) the posted date (`datePosted`).

### Verification (Addendum A)
1. Signed in with at least one scraped job → dashboard **My Jobs** button → `/my-jobs` shows a blue spinner, then job cards.
2. Filter dropdown narrows cards to a single criteria; paging resets to 10.
3. "Load more jobs" appears only when >10 jobs match; reveals 10 more each click up to 100.
4. Each card: board icon, title links out, AI insights populated when enrichment ran, summary expands/collapses, posted date shown.
5. Force a backend failure (e.g. stop the API) → page routes to `/error`.

---

## Addendum B: Tech-skill highlighting from criteria keywords/technology

### Context
Job cards highlight `technologies` blue when they match terms the customer cares about, grey otherwise. The matching terms come from the same `criteria/{customerId}/{criteriaId}.json` blob the scraper reads — not from a separate API.

### `criteria.json` schema addition
```json
{
  "keywords": ["Software Engineer", "C#", "Python", "Azure"],
  "technology": ["React", "GraphQL"],
  "location": "New York, New York"
}
```
- `keywords` — already consumed by the Python scraper as search terms (unchanged).
- `technology` — **new**, addendum-only list of extra terms used purely for job-card highlighting. The scraper never reads this field; only the .NET backend does.

### Backend
- `ICriteriaBlobStorage` / `CriteriaBlobStorage` (see [criteria-integration.md](./criteria-integration.md) — this is the same full criteria-blob reader/writer added for the Criteria CRUD feature) downloads the criteria blob and `JobSeekerController.GetJobs`'s private `GetCombinedKeywordsAsync` helper unions the `Keywords` and `Technology` fields into one case-insensitive, deduped list.
- `CriteriaJobsGroup.Keywords` carries that merged list back to the frontend per criteria group.
- Missing blob (404) or missing field → empty list, so highlighting just falls back to all-grey rather than erroring.

### Frontend
- `UserJobs` matches each `JobTechnology.name` against the merged list (case-insensitive, substring-tolerant in either direction) to set a `matched` flag, sorts matched-first, and renders `matched` as `bg-primary` (blue) vs grey (`bg-secondary`).

---

## Addendum C: `GET /job-seeker/criteria` renamed to `GET /job-seeker/slim-criteria`

As part of the Criteria CRUD feature (see [criteria-integration.md](./criteria-integration.md)), the original `GET /job-seeker/criteria` endpoint described in section 2d above was renamed to `GET /job-seeker/slim-criteria` (`JobSeekerController.GetCriteria` → `GetSlimCriteria`) to make room for a richer `GET /criteria/get-criteria` endpoint on the new `CriteriaController`. The response shape (`{id, name}` pairs) and its use by the Dashboard's scrape-form dropdown are otherwise unchanged — only the route and method name changed.
