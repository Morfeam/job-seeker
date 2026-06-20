import dataclasses
import json
import os
import re
from datetime import datetime, timezone
from zoneinfo import ZoneInfo

_EST = ZoneInfo("America/New_York")

from azure.storage.blob import BlobServiceClient, ContainerClient

from processing.normalize import NormalizedJob

JOB_RESULTS_CONTAINER = "job-seeker-results"
CRITERIA_CONTAINER = "criteria"

_AZURE_CONNECTION_STRING = ""  # placeholder — set AZURE_STORAGE_CONNECTION_STRING env var


def _get_connection_string() -> str:
    return os.environ.get("AZURE_STORAGE_CONNECTION_STRING") or _AZURE_CONNECTION_STRING


def _get_blob_service_client() -> BlobServiceClient:
    return BlobServiceClient.from_connection_string(_get_connection_string())


def load_criteria(customer_id: str, criteria_id: str = "default") -> dict:
    blob_name = f"{customer_id}/{criteria_id}.json"
    client = _get_blob_service_client().get_blob_client(container=CRITERIA_CONTAINER, blob=blob_name)
    return json.loads(client.download_blob().readall())


def blob_file_name(job: NormalizedJob) -> str:
    """Just the blob's file name, e.g. linkedin_li-4327290738_software_engineer__iii.json"""
    sanitized_title = re.sub(r"[^a-z0-9]", "_", job.title.lower())[:50].strip("_")
    return f"{job.source}_{job.id}_{sanitized_title}.json"


def blob_name(job: NormalizedJob, customer_id: str) -> str:
    return f"{customer_id}/{blob_file_name(job)}"


def job_exists(job: NormalizedJob, customer_id: str) -> bool:
    """Returns True if a blob for this job already exists under the customer's folder."""
    client = _get_blob_service_client().get_blob_client(container=JOB_RESULTS_CONTAINER, blob=blob_name(job, customer_id))
    return client.exists()


def save_job(job: NormalizedJob, customer_id: str) -> bool:
    """Upload job to blob storage under the customer's folder. Returns False if already exists."""
    client = _get_blob_service_client().get_blob_client(container=JOB_RESULTS_CONTAINER, blob=blob_name(job, customer_id))
    if client.exists():
        return False
    now_utc = datetime.now(timezone.utc)
    now_est = now_utc.astimezone(_EST)
    formatted = f"{now_est.month}/{now_est.day}/{now_est.year} - {int(now_est.strftime('%I'))}:{now_est.strftime('%M%p')}"
    stamped = dataclasses.replace(job, created_dt=now_utc.isoformat(), last_updated_dt=now_utc.isoformat(), formatted_updated_dt=formatted)
    client.upload_blob(json.dumps(dataclasses.asdict(stamped), indent=2))
    return True
