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
  `--scrape-indeed {true|false} --scrape-linkedin {true|false} --scrape-ziprecruiter false --customer-id {customerId} --criteria-id {criteriaId}`
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
