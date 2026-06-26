# Resume Upload/Download: Web App Self-Service Resume Management

## Context

Customers previously had no way to attach a resume to their job search — the Dashboard's "My Resume" card was a "Coming soon" placeholder. This feature adds a full Resume page (upload / view / download / delete), closely mirroring the Criteria CRUD feature (same JWT/customerId pattern, same dual table+blob storage pattern, same controller conventions) but adding two new pieces: actual file upload (multipart/form-data) and a time-limited SAS download link.

## Storage schema

**Blob**: container `resumes` (renamed from a previously-unused `resume` container — Bicep can't rename a deployed resource in place, so the old empty `resume` container is left orphaned in Azure, harmless), blob path `{customerId}/{fileName}` — the blob name is the **original uploaded file name**, not the resumeId.

**Table** `Resumes`: `PartitionKey` = customerId (guid), `RowKey` = resumeId (guid), `Timestamp` = upload date (built-in system property, not a separate column — same approach `JobLinkTableStorage` already uses), columns `ResumeName` (display name, matches the `CriteriaName` casing convention), `resumeFileName` (original file name), `associatedCriteriaId` (optional — omitted from the entity entirely when not set, rather than written as an empty string, so it reads back cleanly as `null`).

## Backend

### Models (`job-seeker.Server/Models/`)
- `Resume` — internal table-row DTO.
- `ResumeResponseItem` — wire shape for `GET get-resumes` (`id`, `name`, `fileName`, `uploadDate`, `associatedCriteriaId`, `downloadUrl`), built via `ResumeResponseItem.From(Resume row, string downloadUrl)`.
- `AddResumeRequest` — `[FromForm]`-bound POST body: `Name` (optional, `[StringLength(100)]`, falls back to the original filename when blank), `File` (`IFormFile`, `[Required]` — note this only catches a missing file part, not a present-but-zero-length one, so the controller has an explicit `Length == 0` check too), `AssociatedCriteriaId` (optional, unvalidated).
- `DeleteResumeRequest` — `{ ResumeId }`.
- File-size limit (5 MB) is enforced as an inline check in the controller rather than a `ValidationAttribute`, so the error response stays consistent with this feature's other `{ error: "..." }` 400/409 responses instead of producing the generic `ModelState` errors shape.

### Storage (`job-seeker.Server/Operations/Storage/`)
- `IResumeStorage`/`ResumeTableStorage` — `GetByCustomerIdAsync`, `GetByIdAsync` (used by the delete flow to look up the row's `FileName` before the blob can be addressed, since the blob name is the original filename, not the resumeId), `CreateAsync`, `DeleteAsync`.
- `IResumeBlobStorage`/`ResumeBlobStorage` — `UploadAsync` (uses `IfNoneMatch = ETag.All` conditions, i.e. fails with 409 if the blob already exists — defense-in-depth against a blob existing with no matching table row), `DeleteAsync` (idempotent, `DeleteIfExistsAsync`), and `GenerateDownloadUrlAsync(customerId, fileName, validFor)`:
  - Returns `null` if the blob is missing (404-tolerant — `GetResumes` skips these the same way `JobSeekerController.GetJobs` tolerates orphaned `JobLinks` rows).
  - Branches on `BlobClient.CanGenerateSasUri`: **true** (local/connection-string client, holds the account's shared key) builds a `BlobSasBuilder` and calls `GenerateSasUri` directly; **false** (Azure/managed-identity client, no account key) calls `BlobServiceClient.GetUserDelegationKeyAsync` and builds the SAS query string manually via `BlobUriBuilder`.

### Controller — `ResumeController` (`[Route("resume")]`)
- `GET get-resumes` `[Authorize]`: fetches the customer's rows, generates a 1-hour read-only SAS link per row in parallel (`Task.WhenAll`), filters out any row whose blob is missing.
- `POST add` `[Authorize]`: `ModelState` → empty-file check → 5 MB size check → extension allowlist (`.pdf`, `.md`, `.txt`, `.html`, `.docx`) → resolve display name (fallback to filename) → case-insensitive duplicate check against the customer's existing rows on **both** name and filename (409) → upload blob (catching a 409 from the no-overwrite condition as the same duplicate response) → create table row, rolling back the blob if the table write fails (mirrors `CriteriaController.Add`'s exact ordering).
- `POST delete` `[Authorize]`: looks up the row first (`GetByIdAsync`) to learn the filename — 404 if not found — then deletes the blob (idempotent) then the table row.

### `Program.cs`
Registers `IResumeStorage`/`ResumeTableStorage` and `IResumeBlobStorage`/`ResumeBlobStorage` alongside the existing Criteria/JobLink/JobResult singletons.

## Infra (`bicep-templates/main.bicep`)
- Renamed the `resume` container resource to `resumes`.
- Added a `Resumes` table resource (no new table-role assignment needed — `Storage Table Data Contributor` is already scoped at the storage-account level).
- **Added a new `Storage Blob Delegator` role assignment** (`db58b8e5-c6ad-4a2a-8342-4190687cbf4a`) to the managed identity. This was a genuine gap found during planning: `Storage Blob Data Contributor` (already assigned for blob read/write/delete) does **not** include the `generateUserDelegationKey` action needed to mint a user-delegation SAS. Without this role, `GET /resume/get-resumes` works locally (connection-string SAS) but 403s in Azure (delegation SAS) — verify after any redeploy that this endpoint doesn't throw `AuthorizationPermissionMismatch` against the real environment (allow a few minutes for RBAC propagation on the first request after deploy).

## Frontend

- New `Resume` component (`job-seeker.client/src/app/resume/`), route `/resume`. First file-input component in this codebase — Angular reactive forms don't natively bind `<input type="file">`, so the selected `File` is tracked as a plain component property (`selectedFile`) via a `(change)` handler rather than a `FormControl`; the reactive `form` only carries `name` and `associatedCriteriaId`.
- `ngOnInit` fetches `GET /resume/get-resumes` and `GET /job-seeker/slim-criteria` **in parallel via `forkJoin`** (rather than `Dashboard`'s independent-subscribe pattern) so the criteria list is already available to resolve `associatedCriteriaId → name` on first paint.
- Submit builds a `FormData` and POSTs it — critically, no manual `Content-Type` header is set (the browser generates the multipart boundary). On success the form and file input both reset and the list re-fetches. **On a 409 specifically** (duplicate name/filename) the form also resets, distinct from the Criteria page's add-form which stays filled on error.
- Delete is optimistic (immediate local removal, rollback on failure), mirroring `Criteria.onDelete`.
- Cards show name, filename, upload date, the resolved criteria name (falls back to "None"), a direct SAS download link, and a delete button.
- Wired into `app-module.ts`, `app-routing-module.ts`, `proxy.conf.js`, and the Dashboard's "My Resume" card (now a `routerLink="/resume"` button instead of "Coming soon").

## Known accepted risks
1. A narrow TOCTOU race between the table-based duplicate check and the blob's no-overwrite condition — same risk class already accepted in the Criteria feature's name-uniqueness check.
2. Kestrel's default request-body size limits (~30 MB) are well above the 5 MB application-level cap, so no additional `[RequestSizeLimit]` configuration was added — the 5 MB enforcement is purely application-level.

## Verification
1. `dotnet build` / `ng build` — both compile clean.
2. Sign in → Dashboard → "My Resume" → upload a valid small file with a name and criteria selected → confirm the card appears with correct metadata and a working download link.
3. Upload a second resume with a duplicate name, then a duplicate filename → expect 409 + a cleared form both times.
4. Upload a disallowed extension or an oversized file → expect clean 400s.
5. Delete a card → confirm optimistic removal and that a refresh confirms it's actually gone.
6. Post-deploy against Azure: confirm `GET /resume/get-resumes` doesn't 403 on the delegation-key call, confirm blobs land at `resumes/{customerId}/{originalFileName}` and table rows have the expected columns in Storage Explorer, and confirm a copied `downloadUrl` works in an incognito tab (no app JWT) with an `se=` expiry roughly an hour out.
