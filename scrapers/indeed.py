import logging

import jobspy

log = logging.getLogger(__name__)


def scrape_indeed(
    keywords: list[str],
    location: str,
    distance: int | None = None,
    hours_old: int | None = None,
    results_wanted: int = 100,
) -> list[dict]:
    params = dict(
        site_name=["indeed"],
        search_term=" ".join(keywords),
        location=location,
        results_wanted=results_wanted,
    )
    if distance is not None:
        params["distance"] = distance
    if hours_old is not None:
        params["hours_old"] = hours_old
    df = jobspy.scrape_jobs(**params)
    if len(df) == 0:
        log.warning("Indeed returned 0 results — possible bot block or no matches")
    else:
        log.info(f"Indeed: {len(df)} results")
    return df.to_dict(orient="records")
