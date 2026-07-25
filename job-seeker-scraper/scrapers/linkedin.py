import logging
import time

import jobspy

log = logging.getLogger(__name__)

# LinkedIn's public (unauthenticated) job search returns results in pages of 25 and
# throttles further pagination per IP. A single scrape_jobs call reliably returns at
# most ~25 results regardless of results_wanted. We work around this by making 8 calls
# with increasing offsets (0, 20, 40, ..., 140), capped at 20 per call, with a 500 ms delay
# between calls to avoid 429s. This yields up to 160 unique results per run.
# Ref: https://github.com/speedyapply/JobSpy

_CALLS = 8
_RESULTS_PER_CALL = 20
_DELAY_SECONDS = 0.5


def scrape_linkedin(
    keywords: list[str],
    location: str,
    distance: int | None = None,
    hours_old: int | None = None,
) -> list[dict]:
    seen: dict[str, dict] = {}

    for i in range(_CALLS):
        if i > 0:
            time.sleep(_DELAY_SECONDS)

        offset = i * _RESULTS_PER_CALL
        params = dict(
            site_name=["linkedin"],
            search_term=" ".join(keywords),
            location=location,
            results_wanted=_RESULTS_PER_CALL,
            linkedin_fetch_description=True,
            offset=offset,
        )
        if distance is not None:
            params["distance"] = distance
        if hours_old is not None:
            params["hours_old"] = hours_old

        df = jobspy.scrape_jobs(**params)

        if len(df) == 0:
            log.warning(f"LinkedIn call {i + 1}/{_CALLS} (offset={offset}) returned 0 — stopping early")
            break

        log.info(f"LinkedIn call {i + 1}/{_CALLS} (offset={offset}): {len(df)} results")

        for record in df.to_dict(orient="records"):
            job_id = record.get("id")
            key = str(job_id) if job_id else f"noid_{len(seen)}"
            seen.setdefault(key, record)

    log.info(f"LinkedIn total: {len(seen)} unique results")
    return list(seen.values())
