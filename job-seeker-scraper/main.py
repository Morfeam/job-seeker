import argparse
import logging
import os

import anthropic
from dotenv import load_dotenv

load_dotenv()

from scrapers.ziprecruiter import scrape_ziprecruiter
from scrapers.indeed import scrape_indeed
from scrapers.linkedin import scrape_linkedin
from processing.normalize import normalize_job
from processing.enrich import enrich_job
from storage.blob import load_criteria, save_job, job_exists, blob_name
from storage.table import upsert_job
from notifications.email import send_results_email

log = logging.getLogger(__name__)

_LOG_LEVELS = {
    "debug": logging.INFO,
    "standard": logging.WARNING,
    "none": logging.CRITICAL + 1,
}


def _bool_arg(value: str) -> bool:
    return value.lower() != "false"


def setup_and_parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Job Seeker Scraper")
    parser.add_argument("--scrape-ziprecruiter", type=_bool_arg, default=True,
                        metavar="BOOL", help="Scrape ZipRecruiter (default: true)")
    parser.add_argument("--scrape-indeed", type=_bool_arg, default=False,
                        metavar="BOOL", help="Scrape Indeed (default: false)")
    parser.add_argument("--scrape-linkedin", type=_bool_arg, default=False,
                        metavar="BOOL", help="Scrape LinkedIn (default: false, not implemented)")
    parser.add_argument("--what-if", type=_bool_arg, default=False,
                        metavar="BOOL", help="Dry run — skip Azure writes and email (default: false)")
    parser.add_argument("--email", type=str, default=os.environ.get('RECIPIENT_EMAIL', ''),
                        help="Recipient email address for results notification")
    parser.add_argument("--enrich", type=_bool_arg, default=False,
                        metavar="BOOL", help="Use Claude to enrich job descriptions (default: false)")
    parser.add_argument("--log-level", type=str, default="standard", choices=["debug", "standard", "none"],
                        help="Logging verbosity: debug=info+, standard=warning+ only, none=silent (default: standard)")
    parser.add_argument("--customer-id", type=str, default="00000000-0000-0000-0000-000000000000",
                        help="Customer/persona ID — determines criteria file and blob folder (default: empty guid)")
    parser.add_argument("--criteria-id", type=str, default="default",
                        help="Criteria blob ID — determines which criteria file to load (default: 'default')")
    return parser.parse_args()


def _print_summary(scrape_counts: dict[str, int], title_excluded: int, duplicates_skipped: int, saved: int, what_if: bool = False) -> None:
    print("\n" + "=" * 40)
    print("  JOB SEEKER RUN SUMMARY" + (" [WHAT-IF]" if what_if else ""))
    print("=" * 40)
    for src, count in scrape_counts.items():
        print(f"  {src.capitalize():<12} {count} jobs found")
    print(f"  {'Total':<12} {sum(scrape_counts.values())} jobs scraped")
    print(f"  {'Excluded':<12} {title_excluded} removed by title filter")
    print(f"  {'Duplicates':<12} {duplicates_skipped} already in Azure")
    print(f"  {'Saved':<12} {saved} new jobs {'(dry run)' if what_if else 'uploaded to Azure'}")
    print("=" * 40 + "\n")


def main() -> None:
    args = setup_and_parse_args()
    logging.basicConfig(level=_LOG_LEVELS[args.log_level], format="%(levelname)s: %(message)s")

    customer_id = args.customer_id
    log.info(f"Customer ID: {customer_id}")

    log.info("Loading search criteria from Azure Blob Storage...")
    criteria = load_criteria(customer_id, args.criteria_id)
    keywords: list[str] = criteria["keywords"]
    location: str = criteria["location"]
    distance: int | None = criteria.get("distance")
    hours_old: int | None = criteria.get("hoursOld")
    title_exclusions: list[str] = [w.lower() for w in criteria.get("jobTitleExclusionList", [])]
    log.info(f"Criteria — keywords: {keywords}, location: {location}, distance: {distance}, hoursOld: {hours_old}, titleExclusions: {title_exclusions}")

    raw_results: list[dict] = []
    scrape_counts: dict[str, int] = {}

    # TODO: - 'come back and fix or switch to playwright'
    # if args.scrape_ziprecruiter:
    #     log.info("Scraping ZipRecruiter...")
    #     raw_results.extend(scrape_ziprecruiter(keywords, location, results_wanted=10))

    if args.scrape_indeed:
        log.info("Scraping Indeed...")
        indeed_results = scrape_indeed(keywords, location, distance=distance, hours_old=hours_old)
        scrape_counts["indeed"] = len(indeed_results)
        raw_results.extend(indeed_results)

    if args.scrape_linkedin:
        log.info("Scraping LinkedIn...")
        linkedin_results = scrape_linkedin(keywords, location, distance=distance, hours_old=hours_old)
        scrape_counts["linkedin"] = len(linkedin_results)
        raw_results.extend(linkedin_results)

    counts_str = ", ".join(f"{src}: {n}" for src, n in scrape_counts.items())
    log.info(f"Scrape totals — {counts_str} | combined: {len(raw_results)}")

    normalized = [normalize_job(row) for row in raw_results]
    log.info(f"Normalized {len(normalized)} jobs")

    title_excluded = 0
    if title_exclusions:
        before = len(normalized)
        normalized = [
            job for job in normalized
            if not any(word in job.title.lower() for word in title_exclusions)
        ]
        title_excluded = before - len(normalized)
        log.info(f"Title exclusion filter removed {title_excluded} jobs, {len(normalized)} remaining")

    before = len(normalized)
    normalized = [job for job in normalized if not job_exists(job, customer_id)]
    duplicates_skipped = before - len(normalized)
    log.info(f"Dedup: {duplicates_skipped} already in Azure, {len(normalized)} new jobs to process")

    if args.enrich:
        log.info("Enriching job descriptions via Claude...")
        client = anthropic.Anthropic(api_key=os.environ.get("ANTHROPIC_API_KEY"))
        normalized = [enrich_job(job, client) for job in normalized]
        log.info("Enrichment complete")

    if args.what_if:
        log.info("[WHAT-IF] Dry run — skipping Azure writes and email")
        for job in normalized:
            log.info(f"  [{job.source}] {job.title} @ {job.company} | {job.location} | {job.salary}")
        _print_summary(scrape_counts, title_excluded, duplicates_skipped, saved=0, what_if=True)
        return

    log.info("Saving jobs to Azure Blob Storage...")
    saved, skipped = 0, 0
    for job in normalized:
        if save_job(job, customer_id):
            upsert_job(job.id, blob_name(job, customer_id), customer_id)
            saved += 1
        else:
            skipped += 1
    log.info(f"Saved {saved} new jobs, skipped {skipped} duplicates")

    if args.email:
        log.info(f"Sending results email to {args.email}...")
        send_results_email(normalized, args.email)
    else:
        log.info("No --email provided — skipping email")

    _print_summary(scrape_counts, title_excluded, duplicates_skipped, saved=saved)


if __name__ == "__main__":
    main()
