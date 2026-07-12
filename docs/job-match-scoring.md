# Job Match Scoring & Years-of-Experience Filtering

## Context

Scraped jobs need a way to be ranked/filtered by how well they fit a customer's search criteria, so My Jobs can show the best matches first. This started with a keyword/technology point score (`criteriaMatchScore`) and is extended here with a years-of-experience dimension: both a scoring bonus and a client-side visibility filter on My Jobs.

## Where scoring happens

All scoring is computed **once, in Python, at scrape time** (`job-seeker-scraper/main.py`, `_calculate_match_score`), after the optional AI enrichment step, and persisted into the job's blob as `criteria_match_score`. The .NET backend and Angular frontend only ever read and display this value — they never recompute it. This keeps the design consistent with the original keyword/technology scoring.

### Score breakdown
- **+10** per criteria keyword matched (checked against the job's AI-enriched technology names first, then a title+description text fallback)
- **+5** per criteria technology matched (enriched technology names only)
- **+20** if the job's general required years of experience is less than or equal to the criteria's `experienceInYears` — skipped entirely if either value is unknown, since there's nothing to compare

All matching is case-insensitive substring in either direction.

**The years-of-experience bonus is unconditional** — it's calculated the same way regardless of whether a customer has the "Match on Years of Experience" checkbox on/off on My Jobs. That checkbox only controls client-side visibility (see below); it never changes the persisted score.

## Determining a job's general years of experience

`JobResult.YearsExperience` (top-level, not per-technology) was previously a dead field — always `null`, since neither `normalize_job()` nor `enrich_job()` ever set it. It's now populated during AI enrichment (`job-seeker-scraper/processing/enrich.py`), in two steps:

1. **Ask Claude directly.** The enrichment system prompt now asks for an overall `years_experience` integer — the general experience the *role* requires, not tied to any specific technology (e.g. "5+ years of professional software engineering experience" → `5`). Claude is explicitly instructed to return `null` rather than guess when the posting only mentions years in a technology-specific context, or the overall requirement is genuinely ambiguous.
2. **Fallback if Claude returns null.** `enrich_job()` falls back to `max(t.years_experience for t in technologies if not null)` — the highest *required* (not preferred/nice-to-have) years mentioned across the job's extracted technologies. `preferred_years_experience` is deliberately excluded from this fallback so an optional "nice to have 5 years with Kubernetes" doesn't inflate the job's overall required-experience estimate. If there's nothing to fall back to either, the field stays `null`.

This only runs when the scraper is invoked with `--enrich true` (same as the rest of the enrichment fields) — jobs scraped without enrichment keep `years_experience: null`.

## Criteria's `experienceInYears`

Previously disabled on the criteria form (grouped under "More fields (not used yet)"), this field is now live: enabled in `criteria.ts`'s form group, moved into the main visible form area in `criteria.html`, with `Validators.min(0)`/`max(50)`. **It still doesn't affect the actual scrape query** (never passed as a scraper CLI arg) — it's saved to the criteria blob purely for use in scoring and My Jobs filtering, and the form's helper text says as much.

## My Jobs: display and filtering

- **Score and general years of experience are shown on the job card** — `criteriaMatchScore` (top-right corner) and `yearsExperience` ("Experience: N+ years", in the AI Insights section) were already wired up in the card templates from earlier work; they simply had no data to show before this feature. Per-technology years remain the only per-tech display (unchanged, not part of this feature).
- **New "Match on Years of Experience" checkbox** in the Filter & Sort bar, **checked by default**. When checked, `UserJobs.filteredJobs` (`user-jobs.ts`) hides any card where both the job's `yearsExperience` and its criteria's `experienceInYears` are known AND the job's requirement exceeds the criteria's years. **Fails open**: if either value is `null` (undeterminable job, or a criteria that never had a value set), the card stays visible — we don't hide jobs we can't actually evaluate.
- Since `GET /job-seeker/get-jobs` groups jobs by criteria and a customer can view "All criteria" combined, each `CriteriaJobsGroup` now carries its own `experienceInYears` (sourced from the criteria blob, alongside the existing `Keywords` merge — both derived from a single `ICriteriaBlobStorage.GetAsync` call via `JobSeekerController.GetCriteriaDisplayDataAsync`, renamed from the old keywords-only `GetCombinedKeywordsAsync`). Filtering compares each job against *its own* criteria's threshold, not a single global one.
