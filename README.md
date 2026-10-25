# job-seeker

A job search tool that scrapes job boards against your own criteria, scores each posting by how well it matches, and helps you apply with AI-generated cover letters.

It has two parts:

- **Web app** (`job-seeker.Server` + `job-seeker.client`): an ASP.NET Core (.NET 10) API with an Angular 20 front end. Users create an account, save search criteria and resumes, trigger scrapes, browse scored results, and generate cover letters.
- **Scraper** (`job-seeker-scraper`): a containerized Python job that searches Indeed, LinkedIn, and ZipRecruiter, normalizes and scores the results, saves them to Azure Storage, and optionally emails a summary. The web app runs it on demand as an Azure Container App Job.

> **Note on scraping:** automated collection of job postings may violate the terms of service of the job boards being searched. Anyone running or using this project is responsible for complying with those terms and applicable law. See the in-app [Terms of Service](job-seeker.client/src/app/terms/terms.html) for the full disclaimer.

---

## Web App

### Features

- **Accounts**: sign up, sign in (JWT, with optional "remember me"), and password reset by email. Users must accept the Terms of Service to sign up.
- **Search criteria**: save multiple searches with keywords, location and radius, minimum salary, years of experience, job type, technologies, and title exclusions.
- **On-demand scrapes**: trigger a scrape for a saved search and follow its status on the dashboard.
- **Scored results**: *My Jobs* lists results ranked by a match score computed at scrape time from keywords, technologies, and experience. Each job has a details page.
- **Resumes**: upload, view, download, and delete resumes (PDF, DOCX, TXT, MD, HTML; up to 5 MB).
- **AI cover letters**: generate a cover letter for a specific job from a selected resume using Claude, optionally matching the style of your earlier letters.
- **Usage limits**: standard accounts get 3 scrapes and 3 cover letters per UTC day. Admin accounts are exempt.
- **Legal pages**: a collapsible Terms of Service page with deep-linkable sections (`/terms-of-service#privacy`, `#ai`, `#disclaimer`).

### Architecture

```
Angular 20 SPA (Bootstrap 5.3)
        │  served by, and calls
        ▼
ASP.NET Core API (.NET 10) — Azure App Service
   ├── Azure Table Storage   accounts, criteria, resumes, cover letters, scrape runs
   ├── Azure Blob Storage    criteria JSON, resume files, cover letters, job results
   ├── Azure Key Vault       secrets (JWT keys, API keys)
   ├── Anthropic API         cover-letter generation
   ├── Resend                password-reset email
   └── Azure Container App Job ──► job-seeker-scraper (Python)
```

In Azure, the API authenticates to Storage, Key Vault, and the Container App Job with a user-assigned managed identity, so no storage keys are deployed. API endpoints are rate-limited per IP: 100 requests/minute globally, and 10/minute for sign-in and forgot-password.

### Running locally

**Prerequisites:** .NET 10 SDK, Node.js 22, and an Azure Storage account or [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite).

1. Install the client dependencies:

   ```powershell
   cd job-seeker.client
   npm install
   ```

2. Create `job-seeker.Server/local.settings.json` (gitignored) with your local secrets:

   ```json
   {
     "KEY_VAULT_URI": "",
     "storage-connection-string": "<your storage or Azurite connection string>",
     "anthropic-api-key": "<your Anthropic API key>",
     "resend-api-key": "<your Resend API key>"
   }
   ```

   Setting `KEY_VAULT_URI` to an empty string skips Key Vault, so secrets come from this file instead. Development JWT settings are already in `appsettings.Development.json`. To trigger real scrapes from your machine, also set `AZURE_SUBSCRIPTION_ID` and `AZURE_RESOURCE_GROUP`, and sign in with `az login`.

3. Run the API. It launches the Angular dev server through the SPA proxy:

   ```powershell
   dotnet run --project job-seeker.Server
   ```

---

## Scraper

### How it works

1. Loads the search criteria for a customer from Azure Blob Storage
2. Scrapes ZipRecruiter, Indeed, and/or LinkedIn (via [JobSpy](https://github.com/speedyapply/JobSpy))
3. Normalizes results to a common schema (`title`, `company`, `location`, `salary`, `source`, …)
4. Optionally enriches each posting with Claude (required skills, technologies, experience)
5. Calculates a criteria match score for each job
6. Saves the results to Azure Storage and optionally emails the top matches via Resend

### Quick start

```powershell
cd job-seeker-scraper

# Build
docker build -t job-seeker:local .

# Dry run — no Azure or email required
docker run --rm job-seeker:local --what-if true

# Full run
docker run --rm --env-file .env job-seeker:local --email you@example.com
```

Useful flags: `--scrape-indeed`, `--scrape-linkedin`, `--scrape-ziprecruiter`, `--enrich`, `--customer-id`, `--criteria-id`, `--log-level`. See [docs/initial_setup.md](docs/initial_setup.md) for full setup and configuration.

### Dependencies

| Package | Purpose | Docs |
|---|---|---|
| `python-jobspy` | Scrapes ZipRecruiter, Indeed, and LinkedIn | [GitHub](https://github.com/speedyapply/JobSpy) |
| `anthropic` | AI enrichment of job postings | [Docs](https://docs.anthropic.com) |
| `azure-storage-blob` / `azure-data-tables` | Read/write Azure Storage | [Docs](https://learn.microsoft.com/en-us/python/api/azure-storage-blob/azure.storage.blob) |
| `resend` | Send HTML email notifications | [GitHub](https://github.com/resend/resend-python) · [Docs](https://resend.com/docs/send-with-python) |
| `playwright` | Headless browser automation | [Docs](https://playwright.dev/python/docs/intro) |
| `httpx` / `requests` | HTTP clients | [httpx](https://www.python-httpx.org) · [requests](https://requests.readthedocs.io) |
| `beautifulsoup4` | HTML parsing | [Docs](https://www.crummy.com/software/BeautifulSoup/bs4/doc) |
| `python-dotenv` | Load `.env` files locally | [GitHub](https://github.com/theskumar/python-dotenv) |

---

## Deployment

Infrastructure is defined in Bicep (`bicep-templates/`) and deployed by GitHub Actions ([deploy.yml](.github/workflows/deploy.yml)) on every push to `main`. The workflow:

1. Provisions the infrastructure (managed identity, Key Vault, Storage, Container Registry, Container App Environment, App Service)
2. Builds and pushes the scraper image, then deploys the Container App Job
3. Builds the web app and deploys it to App Service

It needs the `AZURE_CREDENTIALS` and `KEY_VAULT_ADMIN_OBJECT_ID` secrets, plus the `AZURE_RESOURCE_GROUP` and `AZURE_DEFAULT_LOCATION` variables. See [docs/infra.md](docs/infra.md).

## Documentation

Design notes for each feature are in [`docs/`](docs/), covering criteria, resumes, scrape integration, match scoring, cover letters, usage quotas, and infrastructure.

## License

[MIT](LICENSE)
