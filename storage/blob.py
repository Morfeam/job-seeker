import dataclasses
import json
import os
import re

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


def save_job(job: NormalizedJob) -> bool:
    """Upload job to blob storage. Returns False if a blob with this job id already exists."""
    sanitized_title = re.sub(r"[^a-z0-9]", "_", job.title.lower())[:50].strip("_")
    filename = f"{job.source}_{job.id}_{sanitized_title}.json"
    container = _get_container_client()
    blob_client = container.get_blob_client(filename)
    if blob_client.exists():
        return False
    blob_client.upload_blob(json.dumps(dataclasses.asdict(job), indent=2))
    return True
