import os

from azure.data.tables import TableServiceClient, TableEntity

TABLE_NAME = "JobLinks"
EMPTY_GUID = "00000000-0000-0000-0000-000000000000"

_AZURE_CONNECTION_STRING = ""  # placeholder — set AZURE_STORAGE_CONNECTION_STRING env var


def _get_table_client():
    conn_str = os.environ.get("AZURE_STORAGE_CONNECTION_STRING") or _AZURE_CONNECTION_STRING
    service = TableServiceClient.from_connection_string(conn_str)
    return service.get_table_client(TABLE_NAME)


def upsert_job(job_id: str, blob_file_name: str, customer_id: str, criteria_id: str = EMPTY_GUID) -> None:
    """Upsert a job into the JobLinks table. Resume and cover letter are empty until assigned.

    blob_file_name is the exact blob file name in job-seeker-results,
    e.g. linkedin_li-4327290738_software_engineer__iii.json
    criteria_id is the RowKey of the CustomerCriteria row that produced this job.
    """
    entity: TableEntity = {
        "PartitionKey": customer_id,
        "RowKey": job_id,
        "customerId": customer_id,
        "BlobFileName": blob_file_name,
        "resumeId": EMPTY_GUID,
        "coverLetterId": EMPTY_GUID,
        "criteriaId": criteria_id,
    }
    _get_table_client().upsert_entity(entity)


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
