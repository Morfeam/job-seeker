# Initial Setup — Job Seeker Scraper

## What Was Built

A containerized Python job scraper with the following capabilities:
- Scrapes ZipRecruiter and Indeed via `python-jobspy`
- Normalizes results to a common `NormalizedJob` schema
- Saves one JSON blob per job to Azure Blob Storage (`job-seeker-results` container)
- Sends a formatted HTML email of the top 5 results via Resend API
- LinkedIn stubbed as `NotImplementedError` — planned for a future prompt

---

## Project Structure

```
job-seeker/
├── Dockerfile
├── requirements.txt
├── main.py                        # Entry point — argparse, orchestration
├── scrapers/
│   ├── __init__.py
│   ├── ziprecruiter.py            # jobspy wrapper for ZipRecruiter
│   ├── indeed.py                  # jobspy wrapper for Indeed
│   └── linkedin.py                # raises NotImplementedError
├── processing/
│   ├── __init__.py
│   └── normalize.py               # NormalizedJob dataclass + normalize_job()
├── storage/
│   ├── __init__.py
│   └── blob.py                    # load_criteria(), save_job() via azure-storage-blob
└── notifications/
    ├── __init__.py
    └── email.py                   # send_results_email() via Resend API
```

---

## CLI Arguments (`main.py`)

| Argument | Type | Default | Description |
|---|---|---|---|
| `--scrape-ziprecruiter` | bool | `true` | Enable ZipRecruiter scraper |
| `--scrape-indeed` | bool | `true` | Enable Indeed scraper |
| `--scrape-linkedin` | bool | `false` | Enable LinkedIn (raises NotImplementedError) |
| `--what-if` | bool | `false` | Dry run — skip Azure writes and email |
| `--email` | string | `""` | Recipient address for results email |

Bool args accept `"true"` / `"false"` strings — Docker-friendly.

---

## Environment Variables Required

| Variable | Used In | Description |
|---|---|---|
| `AZURE_STORAGE_CONNECTION_STRING` | `storage/blob.py` | Azure Blob Storage connection string |
| `RESEND_API_KEY` | `notifications/email.py` | Resend API key |

Both fall back to empty-string placeholders in code. With empty values, the app fails fast on first use with a clear error.

---

## Criteria File (`criteria.json`)

Must exist in the `job-seeker-results` Azure Blob Storage container before running.

```json
{
  "keywords": ["Software Engineer", "C#", "Python", "Azure"],
  "location": "New York, New York",
  "locationBoundaryMiles": 20,
  "salary": null,
  "experienceInYears": 4
}
```

Only `keywords` and `location` are used by the scrapers in this phase.

---

## Key Dependencies

| Package | Purpose |
|---|---|
| `python-jobspy` | Scrapes ZipRecruiter + Indeed (free, open source) |
| `azure-storage-blob` | Load criteria + save results to blob storage |
| `resend` | Send HTML results email |
| `playwright` | Installed for future LinkedIn scraper (Chromium bundled in Docker) |
| `httpx` / `requests` | HTTP clients available for future custom scrapers |
| `beautifulsoup4` | HTML parsing available for future custom scrapers |

---

## Docker Usage

```powershell
# Build
docker build -t job-seeker:local .

# Dry run (no Azure or email needed)
docker run --rm job-seeker:local --what-if true

# Full run with Azure + email
docker run --rm `
  -e AZURE_STORAGE_CONNECTION_STRING="<your-conn-string>" `
  -e RESEND_API_KEY="<your-api-key>" `
  job-seeker:local --email you@example.com

# Disable one scraper
docker run --rm job-seeker:local --what-if true --scrape-indeed false
```

---

## Next Steps

- [ ] Supply `AZURE_STORAGE_CONNECTION_STRING` and upload `criteria.json` to blob container
- [ ] Supply `RESEND_API_KEY` and verify `joeprints.shop` domain in Resend dashboard
- [ ] Implement LinkedIn scraper (separate prompt — requires Playwright + session cookies)
- [ ] Add source icons to email cards (100x100px, keyed by `job.source`)
- [ ] Add LLM scoring against resume (future phase)
- [ ] Add auto-apply logic (future phase)
