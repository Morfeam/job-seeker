# Criteria CRUD: Web App Self-Service Search Criteria

## Context

Previously, a customer's "criteria" object — the keywords/location/filters the Python scraper reads to run a job search — could only be created or removed by hand-editing blob/table storage. This feature adds a full Criteria page (view / add / delete) to the web app so customers can self-serve their own search criteria, while keeping the underlying storage formats fully compatible with what the Python scraper already reads.

It also supersedes the `ICriteriaKeywordsStorage`/`CriteriaKeywordsBlobStorage` classes (added to power tech-skill highlighting on the My Jobs page) — replaced by a full strongly-typed criteria-blob reader/writer (`ICriteriaBlobStorage`/`CriteriaBlobStorage`) that both this feature and the My Jobs highlighting logic now share.

## `criteria.json` blob schema

Stored at `criteria/{customerId}/{criteriaId}.json` (container `criteria`), same path convention the Python scraper (`job-seeker-scraper/main.py`) already reads:

```json
{
  "keywords": ["C#", "Azure"],
  "jobTitleExclusionList": ["lead", "manager", "architect", "director", "intern"],
  "location": "White Plains, NY",
  "technology": ["C#", "Azure", "Databricks"],
  "distance": 20,
  "minSalary": 100000.00,
  "experienceInYears": 4,
  "jobType": "fulltime",
  "hoursOld": 504
}
```

**Fields the scraper actually reads** (verified against `main.py`, not just docs): `keywords` (required key), `location` (required key), `distance`, `hoursOld`, `jobTitleExclusionList` — all consumed verbatim, so their key names/casing here are load-bearing.

**Webapp-only fields, not read by the scraper at all**: `technology`, `minSalary`, `experienceInYears`, `jobType`. These exist in the same JSON blob for storage/display purposes only — the scraper silently ignores them today. Wiring them into actual scrape behavior (e.g. having `minSalary`/`jobType` filter results) is out of scope for this feature and would require separate changes to `job-seeker-scraper`.

The `CustomerCriteria` table only ever stores `criteriaId` (RowKey) + `CriteriaName` — everything else lives in the blob.

## Backend

### Models (`job-seeker.Server/Models/`)
- `CriteriaDetail` — the blob contract, `[JsonPropertyName]`-pinned to the schema above.
- `CriteriaResponseItem` — flat `{id, name, ...CriteriaDetail fields}` response shape for `GET /criteria/get-criteria`, built via `CriteriaResponseItem.From(id, name, detail)`.
- `AddCriteriaRequest` — POST `/criteria/add` body. `Name` required (3–50 chars), `Location` required (the scraper's hard `criteria["location"]` access means it must always be present). `Keywords`/`JobTitleExclusionList` capped via a custom `[MaxCount(n)]` attribute (5 and 10 respectively) — not required, since an empty list is valid and still satisfies the scraper's required *key*. `Technology` uncapped.
- `MaxCountAttribute` — reusable `ValidationAttribute` checking `ICollection.Count <= max`.
- `DeleteCriteriaRequest` — `{ CriteriaId }`.

### Storage (`job-seeker.Server/Operations/Storage/`)
- `ICriteriaBlobStorage`/`CriteriaBlobStorage` — `GetAsync`, `UpsertAsync`, `DeleteAsync` against the `criteria` container, reading/writing the full `CriteriaDetail` object. `DeleteAsync` uses `DeleteIfExistsAsync` (idempotent — a missing blob is treated as already-deleted).
- `ICriteriaStorage`/`CriteriaTableStorage` — added `CreateAsync(customerId, criteriaId, name)` and `DeleteAsync(customerId, criteriaId)` alongside the existing `GetByCustomerIdAsync`.

### Controllers
- **New `CriteriaController`** (`[Route("criteria")]`), all endpoints `[Authorize]`, customerId resolved from the JWT (same `ClaimTypes.NameIdentifier`/`sub` pattern as `JobSeekerController`):
  - `GET get-criteria` — fetches table rows for the customer, fetches each row's blob in parallel (`Task.WhenAll`), merges into `CriteriaResponseItem`s, returns the array.
  - `POST add` — validates `ModelState` and the `jobType` allowed set (`fulltime`/`parttime`/`contract`/`internship`); checks case-insensitive name uniqueness across the customer's existing criteria (409 on collision — a benign best-effort check, not a transactional guarantee, since Table Storage has no native unique secondary index); generates a new `Guid` criteriaId; writes the blob, then the table row. If the table write fails after the blob succeeded, the blob is deleted as a rollback before returning 409, so a criteria is never left without a discoverable name.
  - `POST delete` — deletes the blob (idempotent), then the table row; 409 on either failure.
- **`JobSeekerController`**: `GetCriteria()` renamed to `GetSlimCriteria()`, route `/job-seeker/criteria` → `/job-seeker/slim-criteria` (response shape unchanged — still the slim `{id, name}` list used by the Dashboard's scrape-form dropdown). `GetJobs()` now derives its tech-highlighting keyword list via a private `GetCombinedKeywordsAsync` helper that reads the full `CriteriaDetail` via `ICriteriaBlobStorage` and unions `keywords` + `technology` (reproducing the old `CriteriaKeywordsBlobStorage` behavior exactly).

## Frontend

- **New `ChipInput` component** (`job-seeker.client/src/app/chip-input/`) — a `ControlValueAccessor` that drops into `formControlName` like a native input. Type a value, press Enter/comma to commit it as a removable chip; an optional `maxItems` input stops accepting new chips at the cap. Used for `keywords`, `jobTitleExclusionList`, and `technology` in the add form.
- **New `Criteria` component** (`job-seeker.client/src/app/criteria/`), route `/criteria`. Visual style mirrors the My Jobs page (banner, spinner, `row g-4`/`col-12 col-lg-6` card grid).
  - `ngOnInit` auth-guards (redirect to `/sign-in` if no token) then calls `GET /criteria/get-criteria`.
  - Add form mirrors `AddCriteriaRequest` field-for-field, with `maxChipCountValidator(5|10)` (new `chip-count-validator.ts`) backing the chip-input caps client-side, and a fixed `<select>` for `jobType`. On successful `POST /criteria/add`, the form resets and the full criteria list is re-fetched from the backend (not an optimistic append), so the new card reflects the server-assigned id.
  - Delete is optimistic: the card is removed from the local list immediately on click, with the `POST /criteria/delete` call firing in the background; only on failure is the previous list restored and an error shown.
- **Dashboard**: the "Search Criteria" card's "Coming soon" badge is replaced with a `routerLink="/criteria"` button (mirrors the existing "My Jobs" card button). The scrape-form's criteria dropdown now fetches `/job-seeker/slim-criteria` instead of `/job-seeker/criteria`.
- **`proxy.conf.js`**: added a `/criteria` proxy entry alongside the existing `/job-seeker`/`/customer` entries for local dev.

## Verification

1. `dotnet build` / `ng build` — both compile clean; no remaining references to `ICriteriaKeywordsStorage`/`CriteriaKeywordsBlobStorage` anywhere in the repo.
2. Sign in → Dashboard → "Search Criteria" → `/criteria` loads (empty state if none exist yet).
3. Submit the add form with a full payload (name, location, a few keywords/technology/exclusions, distance/minSalary/experienceInYears/hoursOld, a jobType) → 200, form resets, the new card appears (proves the full re-fetch path). Confirm in storage: a blob at `criteria/{customerId}/{guid}.json` with the exact scraper-expected key casing, and a matching `CustomerCriteria` row.
4. Duplicate name (any case) → 409 + inline error. A 6th keyword is refused by the chip input in-UI; a direct API call bypassing the UI with 6 keywords gets a 400. An invalid `jobType` via direct API call gets a 400.
5. Click delete on a card → disappears immediately; confirm both the blob and table row are gone in storage; reload the page and confirm it doesn't reappear.
6. On `/my-jobs`, confirm tech-tag blue/grey highlighting still works (regression check on the `GetCombinedKeywordsAsync` consolidation).
7. Trigger a scrape (existing `/job-seeker/scrape` flow, unchanged) against a criteria created through the new UI and confirm the Python container reads `keywords`/`location`/`distance`/`hoursOld`/`jobTitleExclusionList` without error.
