# Daily Usage Quotas

## Context

Scraping and cover-letter generation are the two most costly actions in this app — scraping triggers a Container App Job, and cover-letter generation calls the Anthropic API directly. This feature caps both to 3 per customer per UTC calendar day for standard customers, with an `isAdmin` flag to exempt trusted accounts from the cap entirely.

## Schema

Five new columns on the existing `Customers` table (PartitionKey = email, RowKey = customerId):

| Column | Type | Purpose |
|---|---|---|
| `IsAdmin` | bool | Bypasses both daily caps entirely when `true`. |
| `ScrapeCount` | int | Scrapes triggered so far in the current UTC day. |
| `CoverLetterCount` | int | Cover letters generated so far in the current UTC day. |
| `LastScrapeDate` | DateTimeOffset | UTC timestamp of the most recent successful scrape. |
| `LastCoverLetterDate` | DateTimeOffset | UTC timestamp of the most recent successful cover-letter generation. |

No migration is needed for existing customer rows — missing columns read as `false`/`0`/`null` via `TableEntity.GetBoolean`/`GetInt32`/`GetDateTimeOffset`, which is exactly the "no usage yet" state.

**Becoming an admin is a manual, out-of-band operation** — there is no promotion endpoint. Set `IsAdmin=true` directly on a customer's row via Azure Storage Explorer, the Portal, or the CLI.

## The count column only means anything relative to the date column

`ScrapeCount`/`CoverLetterCount` are not lifetime counters — they represent "usage so far today." Every read of "how much quota has this customer used" first checks whether the corresponding `LastXDate` falls on the current UTC calendar date; if it doesn't (i.e., the last use was yesterday or earlier, or never), the count is treated as `0` regardless of what's actually stored. This means the day boundary is UTC midnight, not a rolling 24 hours from the customer's last action — a customer who scrapes at 11:59pm UTC can scrape again 1 minute later.

This logic lives in two private helpers on `CustomerTableStorage`:
- `TodayUsageCount(entity, countColumn, dateColumn)` — the reset-aware read.
- `HasQuotaRemainingAsync(customerId, countColumn, dateColumn, dailyLimit)` — read-only, admin-bypass-aware, used to gate the costly action *before* it runs.
- `RecordUsageAsync(customerId, countColumn, dateColumn)` — the write: increments (with the same reset logic) and stamps `UtcNow`, using ETag-based optimistic concurrency with a few retries so two near-simultaneous requests can't both slip past the cap. Called for admins too, since these columns double as usage stats regardless of enforcement.

`ICustomerStorage` exposes this as four purpose-specific methods (`HasScrapeQuotaRemainingAsync`, `HasCoverLetterQuotaRemainingAsync`, `RecordScrapeAsync`, `RecordCoverLetterAsync`) rather than a generic enum-driven pair, consistent with this codebase's existing preference for concrete, single-purpose storage methods over generic ones.

## Only successful actions count

Per the explicit requirement, a scrape or cover-letter generation only increments its counter if it actually succeeded — a failed attempt doesn't cost the customer a slot.

- **`POST /job-seeker/scrape`**: the quota is checked (read-only) before any Container App Job work begins, so a customer with no quota left never triggers the expensive ARM call. The counter is only incremented immediately before returning `202 Accepted`, i.e., once `job.StartAsync` has actually succeeded. Note this endpoint is fire-and-forget against an asynchronous job — "success" here means "the trigger call succeeded," not "the scrape finished" or "jobs were found," since the web app has no visibility into the Container App Job's actual completion.
- **`POST /cv/generate`**: the quota is checked immediately after resolving the customer from the JWT, before any resume/job lookups or the Anthropic call. The counter is only incremented right before the final `200 OK`, after the cover letter has been generated, uploaded, and its table row committed.

## Cap-exceeded response

Both endpoints return `429 Too Many Requests` with `{ "error": "You've reached your daily limit of 3 <scrapes|cover letters>. Please check back tomorrow." }` when quota is exhausted. This is distinct from the app's existing global rate limiter (`Program.cs`, 100 requests/minute per IP, also `429`) — that one guards against raw request-volume abuse; this one is a business-level daily quota tied to a specific customer. No frontend changes were needed to surface this: `dashboard.ts`'s scrape handler and `cover-letter-generator.ts`/`job-details.ts`'s generate handlers already display `err.error?.error` from any non-2xx response, so the quota message surfaces automatically through the existing generic error-toast/inline-error paths.
