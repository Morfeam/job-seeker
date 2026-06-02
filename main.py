import argparse
import logging
import os

from scrapers.ziprecruiter import scrape_ziprecruiter
from scrapers.indeed import scrape_indeed
from scrapers.linkedin import scrape_linkedin
from processing.normalize import normalize_job
from storage.blob import load_criteria, save_job
from notifications.email import send_results_email

logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
log = logging.getLogger(__name__)


def _bool_arg(value: str) -> bool:
    return value.lower() != "false"


def setup_and_parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Job Seeker Scraper")
    parser.add_argument("--scrape-ziprecruiter", type=_bool_arg, default=True,
                        metavar="BOOL", help="Scrape ZipRecruiter (default: true)")
    parser.add_argument("--scrape-indeed", type=_bool_arg, default=True,
                        metavar="BOOL", help="Scrape Indeed (default: true)")
    parser.add_argument("--scrape-linkedin", type=_bool_arg, default=False,
                        metavar="BOOL", help="Scrape LinkedIn (default: false, not implemented)")
    parser.add_argument("--what-if", type=_bool_arg, default=False,
                        metavar="BOOL", help="Dry run — skip Azure writes and email (default: false)")
    parser.add_argument("--email", type=str, default=os.environ.get('RECIPIENT_EMAIL', ''),
                        help="Recipient email address for results notification")
    return parser.parse_args()


def main() -> None:
    args = setup_and_parse_args()

    log.info("Loading search criteria from Azure Blob Storage...")
    criteria = load_criteria()
    keywords: list[str] = criteria["keywords"]
    location: str = criteria["location"]
    distance: int | None = criteria.get("distance")
    hours_old: int | None = criteria.get("hoursOld")
    title_exclusions: list[str] = [w.lower() for w in criteria.get("jobTitleExclusionList", [])]
    log.info(f"Criteria — keywords: {keywords}, location: {location}, distance: {distance}, hoursOld: {hours_old}, titleExclusions: {title_exclusions}")

    raw_results: list[dict] = []

    # TODO: - 'come back and fix or switch to playwright'
    # if args.scrape_ziprecruiter:
    #     log.info("Scraping ZipRecruiter...")
    #     raw_results.extend(scrape_ziprecruiter(keywords, location, results_wanted=10))

    if args.scrape_indeed:
        log.info("Scraping Indeed...")
        raw_results.extend(scrape_indeed(keywords, location, distance=distance, hours_old=hours_old))

    if args.scrape_linkedin:
        scrape_linkedin()  # raises NotImplementedError

    log.info(f"Total raw results: {len(raw_results)}")

    normalized = [normalize_job(row) for row in raw_results]
    log.info(f"Normalized {len(normalized)} jobs")

    if title_exclusions:
        before = len(normalized)
        normalized = [
            job for job in normalized
            if not any(word in job.title.lower() for word in title_exclusions)
        ]
        log.info(f"Title exclusion filter removed {before - len(normalized)} jobs, {len(normalized)} remaining")

    if args.what_if:
        log.info("[WHAT-IF] Dry run — skipping Azure writes and email")
        for job in normalized:
            log.info(f"  [{job.source}] {job.title} @ {job.company} | {job.location} | {job.salary}")
        return

    log.info("Saving jobs to Azure Blob Storage...")
    saved, skipped = 0, 0
    for job in normalized:
        if save_job(job):
            saved += 1
        else:
            skipped += 1
    log.info(f"Saved {saved} new jobs, skipped {skipped} duplicates")

    if args.email:
        log.info(f"Sending results email to {args.email}...")
        send_results_email(normalized, args.email)
    else:
        log.info("No --email provided — skipping email")


if __name__ == "__main__":
    main()
