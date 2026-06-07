import dataclasses
import json
import os
import re
from datetime import datetime, timezone
from zoneinfo import ZoneInfo

_EST = ZoneInfo("America/New_York")

from azure.storage.blob import BlobServiceClient, ContainerClient

from processing.normalize import NormalizedJob

CONTAINER_NAME = "job-seeker-results"
CRITERIA_BLOB_NAME = "criteria.json"

_AZURE_CONNECTION_STRING = ""  # placeholder — set AZURE_STORAGE_CONNECTION_STRING env var


def _get_connection_string() -> str:
    return os.environ.get("AZURE_STORAGE_CONNECTION_STRING") or _AZURE_CONNECTION_STRING


def _get_container_client() -> ContainerClient:
    conn_str = _get_connection_string()
    client = BlobServiceClient.from_connection_string(conn_str)
    return client.get_container_client(CONTAINER_NAME)


def load_criteria() -> dict:
    container = _get_container_client()
    blob_data = container.get_blob_client(CRITERIA_BLOB_NAME).download_blob().readall()
    return json.loads(blob_data)


def _blob_filename(job: NormalizedJob) -> str:
    sanitized_title = re.sub(r"[^a-z0-9]", "_", job.title.lower())[:50].strip("_")
    return f"{job.source}_{job.id}_{sanitized_title}.json"


def job_exists(job: NormalizedJob) -> bool:
    """Returns True if a blob for this job id already exists in storage."""
    return _get_container_client().get_blob_client(_blob_filename(job)).exists()


def save_job(job: NormalizedJob) -> bool:
    """Upload job to blob storage. Returns False if a blob with this job id already exists."""
    container = _get_container_client()
    blob_client = container.get_blob_client(_blob_filename(job))
    if blob_client.exists():
        return False
    now_utc = datetime.now(timezone.utc)
    now_est = now_utc.astimezone(_EST)
    last_updated_est = f"{now_est.month}/{now_est.day}/{now_est.year} - {int(now_est.strftime('%I'))}:{now_est.strftime('%M%p')}"
    stamped = dataclasses.replace(job, created_dt=now_utc.isoformat(), last_updated_dt=now_utc.isoformat(), formatted_updated_dt=last_updated_est)
    blob_client.upload_blob(json.dumps(dataclasses.asdict(stamped), indent=2))
    return True
