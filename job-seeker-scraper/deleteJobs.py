import argparse
import os
from datetime import datetime, timezone, timedelta

from dotenv import load_dotenv
from azure.storage.blob import BlobServiceClient

load_dotenv()

CONTAINER_NAME = "job-seeker-results"

_AZURE_CONNECTION_STRING = ""  # placeholder — set AZURE_STORAGE_CONNECTION_STRING env var


def _get_connection_string() -> str:
    return os.environ.get("AZURE_STORAGE_CONNECTION_STRING") or _AZURE_CONNECTION_STRING


def _get_container_client():
    return BlobServiceClient.from_connection_string(_get_connection_string()).get_container_client(CONTAINER_NAME)


def parse_date(value: str) -> datetime:
    try:
        return datetime.strptime(value, "%m/%d/%Y").replace(tzinfo=timezone.utc)
    except ValueError:
        raise argparse.ArgumentTypeError(f"Invalid date '{value}' — expected format: M/D/YYYY (e.g. 3/28/2026)")


def main() -> None:
    parser = argparse.ArgumentParser(description="Delete job blobs from Azure by date range")
    parser.add_argument("--start-date", type=parse_date, required=True,
                        metavar="M/D/YYYY", help="Delete blobs created on or after this date (inclusive)")
    parser.add_argument("--end-date", type=parse_date, required=True,
                        metavar="M/D/YYYY", help="Delete blobs created on or before this date (inclusive)")
    parser.add_argument("--what-if", action="store_true",
                        help="Preview blobs that would be deleted without actually deleting them")
    args = parser.parse_args()

    if args.start_date > args.end_date:
        print(f"ERROR: --start-date must be before --end-date")
        return

    end_date_inclusive = args.end_date + timedelta(days=1)

    container = _get_container_client()

    blobs_in_range = [
        blob for blob in container.list_blobs()
        if blob.name != "criteria.json"
        and blob.last_modified is not None
        and args.start_date <= blob.last_modified.replace(tzinfo=timezone.utc) < end_date_inclusive
    ]

    if not blobs_in_range:
        print(f"No blobs found between {args.start_date.strftime('%m/%d/%Y')} and {args.end_date.strftime('%m/%d/%Y')}.")
        return

    label = "[WHAT-IF] " if args.what_if else ""
    print(f"{label}Found {len(blobs_in_range)} blob(s) in range {args.start_date.strftime('%m/%d/%Y')} – {args.end_date.strftime('%m/%d/%Y')}:")
    for blob in blobs_in_range:
        ts = blob.last_modified.strftime("%m/%d/%Y %I:%M%p") if blob.last_modified else "unknown"
        print(f"  {blob.name}  (uploaded: {ts})")

    if args.what_if:
        print("\n[WHAT-IF] No blobs were deleted. Remove --what-if to perform the deletion.")
        return

    confirm = input(f"\nDelete {len(blobs_in_range)} blob(s)? [y/N] ").strip().lower()
    if confirm != "y":
        print("Aborted.")
        return

    deleted, failed = 0, 0
    for blob in blobs_in_range:
        try:
            container.delete_blob(blob.name)
            print(f"  Deleted: {blob.name}")
            deleted += 1
        except Exception as e:
            print(f"  FAILED:  {blob.name} — {e}")
            failed += 1

    print(f"\nDone. Deleted: {deleted}, Failed: {failed}")


if __name__ == "__main__":
    main()
