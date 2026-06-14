import logging

import jobspy

log = logging.getLogger(__name__)


def scrape_ziprecruiter(keywords: list[str], location: str, results_wanted: int = 10) -> list[dict]:
    df = jobspy.scrape_jobs(
        site_name=["zip_recruiter"],
        search_term=" ".join(keywords),
        location=location,
        results_wanted=results_wanted,
    )
    if len(df) == 0:
        log.warning("ZipRecruiter returned 0 results — possible bot block or no matches")
    else:
        log.info(f"ZipRecruiter: {len(df)} results")
    return df.to_dict(orient="records")
