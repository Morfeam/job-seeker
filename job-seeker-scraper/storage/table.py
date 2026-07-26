import os

from azure.core.exceptions import ResourceNotFoundError
from azure.data.tables import TableServiceClient, TableEntity

TABLE_NAME = "JobLinks"
EMPTY_GUID = "00000000-0000-0000-0000-000000000000"

_AZURE_CONNECTION_STRING = ""  # placeholder — set AZURE_STORAGE_CONNECTION_STRING env var


def _get_table_client():
    conn_str = os.environ.get("AZURE_STORAGE_CONNECTION_STRING") or _AZURE_CONNECTION_STRING
    service = TableServiceClient.from_connection_string(conn_str)
    return service.get_table_client(TABLE_NAME)


def upsert_job(
    job_id: str,
    blob_file_name: str,
    customer_id: str,
    criteria_id: str = EMPTY_GUID,
    criteria_match_score: int | None = None,
    years_experience: int | None = None,
) -> None:
    """Upsert a job into the JobLinks table, linking it to the criteria that found it.
    Resume and cover letter are empty until assigned.

    blob_file_name is the exact blob file name in job-seeker-results,
    e.g. linkedin_li-4327290738_software_engineer__iii.json
    criteria_id is the RowKey of the CustomerCriteria row that produced this job.

    A customer's first-ever link to a given job keeps the plain job_id RowKey
    (unchanged from before, so every pre-existing row keeps working exactly as-is).
    If a *different* criteria later matches that same job, it gets an additional
    row (RowKey prefixed with this criteria_id) rather than overwriting the first
    criteria's row — the same job can be scored independently per criteria. A
    re-scrape by the *same* criteria just refreshes its existing row in place,
    which also self-heals a legacy row into carrying the score/years columns.
    """
    table = _get_table_client()

    row_key = job_id
    try:
        existing = table.get_entity(partition_key=customer_id, row_key=job_id)
        if existing.get("criteriaId") != criteria_id:
            row_key = f"{criteria_id}_{job_id}"
    except ResourceNotFoundError:
        pass

    entity: TableEntity = {
        "PartitionKey": customer_id,
        "RowKey": row_key,
        "customerId": customer_id,
        "BlobFileName": blob_file_name,
        "resumeId": EMPTY_GUID,
        "coverLetterId": EMPTY_GUID,
        "criteriaId": criteria_id,
        "criteriaMatchScore": criteria_match_score if criteria_match_score is not None else 0,
    }
    if years_experience is not None:
        entity["yearsExperience"] = years_experience

    table.upsert_entity(entity)


def upsert_job_link(
    customer_id: str,
    blob_file_name: str,
    resume_id: str | None = None,
    cover_letter_id: str | None = None,
    criteria_id: str | None = None,
) -> None:
    """Insert or update a JobLinks record linking a job posting to its related artifacts."""
    entity: TableEntity = {
        "PartitionKey": customer_id,
        "RowKey": blob_file_name,
        "customerId": customer_id,
        "BlobFileName": blob_file_name,
        "resumeId": resume_id or "",
        "coverLetterId": cover_letter_id or "",
        "criteriaId": criteria_id or "",
    }
    _get_table_client().upsert_entity(entity)


def get_job_links(customer_id: str) -> list[TableEntity]:
    """Return all JobLinks rows for a given customer."""
    table = _get_table_client()
    return list(table.query_entities(f"PartitionKey eq '{customer_id}'"))
