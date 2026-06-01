# Infrastructure

The job-seeker app runs as an Azure Container App Job on a daily CRON schedule (9 AM EST). All infrastructure is provisioned via Bicep and deployed through GitHub Actions.

## Architecture

```
GitHub Actions (deploy.yml)
│
├── Job 1: deploy-infra  →  main.bicep
│     ├── User-Assigned Managed Identity (job-seeker-uai-dev)
│     ├── Key Vault (job-seeker-kv-dev)
│     ├── Storage Account (jobseekerdeveastus2)
│     │     └── Blob Container: job-seeker-results
│     └── module: container-env.bicep
│           ├── Log Analytics Workspace (job-seeker-law-dev)
│           ├── Container Registry (jobseekeracrdeveastus2)
│           └── Container App Environment (job-seeker-env-dev)
│
└── Job 2: publish-docker  →  container-job.bicep
      ├── Build & push Docker image to ACR
      └── Container App Job (job-seeker-job-dev)
            CRON: 0 14 * * *  (9 AM EST daily)
```

## Resources

| Resource | Name (dev) | Notes |
|----------|-----------|-------|
| User-Assigned Identity | `job-seeker-uai-dev` | Identity for all container auth |
| Key Vault | `job-seeker-kv-dev` | Secrets added manually via Portal |
| Storage Account | `jobseekerdev<location>` | Standard LRS |
| Blob Container | `job-seeker-results` | Hardcoded in `storage/blob.py` |
| Log Analytics | `job-seeker-law-dev` | Container job logs |
| Container Registry | `jobseekeracr<dev><location>` | Basic SKU, UAI auth |
| Container App Environment | `job-seeker-env-dev` | No public ingress |
| Container App Job | `job-seeker-job-dev` | CRON triggered |

## Bicep Files

- [bicep-templates/main.bicep](../bicep-templates/main.bicep) — UAI, Key Vault, Storage Account, calls container-env module
- [bicep-templates/container-env.bicep](../bicep-templates/container-env.bicep) — Log Analytics, ACR, Container App Environment
- [bicep-templates/container-job.bicep](../bicep-templates/container-job.bicep) — Container App Job (deployed separately in CI after Docker push)

## GitHub Actions

Workflow: [.github/workflows/deploy.yml](../.github/workflows/deploy.yml)

Triggers: push to `main`, PRs to `main`, manual dispatch.

**Required GitHub Secrets:**
- `AZURE_CREDENTIALS` — Service principal JSON
- `KEY_VAULT_ADMIN_OBJECT_ID` — Your Azure AD object ID (find in Azure Portal → Entra ID → Users → your account → Object ID)

**Required GitHub Variables:**
- `AZURE_RESOURCE_GROUP` — e.g., `job-seeker-dev`
- `AZURE_DEFAULT_LOCATION` — e.g., `eastus2`

## First-Time Setup

After the first successful CI/CD run, complete these manual steps before the first CRON execution:

### 1. Add Key Vault Secrets

Navigate to **Key Vault → job-seeker-kv-dev → Secrets → Generate/Import** and add:

| Secret Name | Value |
|-------------|-------|
| `storage-connection-string` | Copy from Storage Account → Security + Networking → Access Keys |
| `resend-api-key` | From your [Resend.com](https://resend.com) dashboard |
| `recipient-email` | The email address to receive daily job results |

### 2. Upload criteria.json

Navigate to **Storage Account → Containers → job-seeker-results → Upload** and upload a `criteria.json` file:

```json
{
  "keywords": ["Software Engineer", "C#", "Python", "Azure"],
  "location": "New York, New York",
  "distance": 25,
  "hoursOld": 72,
  "jobType": "fulltime",
  "jobTitleExclusionList": ["senior", "staff", "principal", "manager", "director"]
}
```

## Verify a Run

1. Azure Portal → Container Apps → job-seeker-job-dev → Run Now
2. Check **Execution History** for status
3. Check **Log Analytics** for container output
4. Verify results email arrives at the configured recipient address
