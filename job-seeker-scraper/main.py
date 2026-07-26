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
from storage.blob import load_criteria, save_job, load_job, job_exists, blob_file_name
from storage.table import upsert_job
from notifications.email import send_results_email

log = logging.getLogger(__name__)


def _calculate_match_score(job, keywords: list[str], technology: list[str], experience_in_years: int | None) -> int:
    """Score a job against the criteria that found it.

    Each matched keyword scores 10 pts (checked against the job's AI-enriched
    technology names first, then the job title + description as a fallback).
    Each matched technology entry scores 5 pts (enriched technology names only).
    Matching is case-insensitive substring in either direction.
    A job whose (general, AI-determined) required years of experience is less than
    or equal to the criteria's experienceInYears scores a flat +20 pts bonus —
    skipped entirely if either value is unknown, since there's nothing to compare.
    """
    tech_names = [t.name.lower() for t in (job.technologies or [])]
    job_text = f"{job.title or ''} {job.description or ''}".lower()
    score = 0

    for kw in keywords:
        kw_lower = kw.lower()
        in_tech = any(kw_lower in t or t in kw_lower for t in tech_names)
        in_text = kw_lower in job_text
        if in_tech or in_text:
            score += 10

    for tech in technology:
        tech_lower = tech.lower()
        if any(tech_lower in t or t in tech_lower for t in tech_names):
            score += 5

    if experience_in_years is not None and job.years_experience is not None and job.years_experience <= experience_in_years:
        score += 20

    return score


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


def _print_summary(scrape_counts: dict[str, int], title_excluded: int, company_excluded: int, already_captured: int, saved: int, linked: int = 0, what_if: bool = False) -> None:
    print("\n" + "=" * 40)
    print("  JOB SEEKER RUN SUMMARY" + (" [WHAT-IF]" if what_if else ""))
    print("=" * 40)
    for src, count in scrape_counts.items():
        print(f"  {src.capitalize():<12} {count} jobs found")
    print(f"  {'Total':<12} {sum(scrape_counts.values())} jobs scraped")
    print(f"  {'Excluded':<12} {title_excluded} removed by title filter")
    print(f"  {'Excluded':<12} {company_excluded} removed by company filter")
    print(f"  {'Existing':<12} {already_captured} already in Azure (from another criteria/run)")
    print(f"  {'Saved':<12} {saved} new jobs {'(dry run)' if what_if else 'uploaded to Azure'}")
    print(f"  {'Linked':<12} {linked} existing jobs newly linked to this criteria{' (dry run)' if what_if else ''}")
    print("=" * 40 + "\n")


def main() -> None:
    args = setup_and_parse_args()
    logging.basicConfig(level=_LOG_LEVELS[args.log_level], format="%(levelname)s: %(message)s")

    customer_id = args.customer_id
    log.info(f"Customer ID: {customer_id}")

    log.info("Loading search criteria from Azure Blob Storage...")
    criteria = load_criteria(customer_id, args.criteria_id)
    keywords: list[str] = criteria["keywords"]
    technology: list[str] = criteria.get("technology", [])
    experience_in_years: int | None = criteria.get("experienceInYears")
    location: str = criteria["location"]
    distance: int | None = criteria.get("distance")
    hours_old: int | None = criteria.get("hoursOld")
    title_exclusions: list[str] = [w.lower() for w in criteria.get("jobTitleExclusionList", [])]
    company_exclusions: list[str] = [w.lower() for w in criteria.get("companyNameExclusionList", [])]
    log.info(f"Criteria — keywords: {keywords}, location: {location}, distance: {distance}, hoursOld: {hours_old}, titleExclusions: {title_exclusions}, companyExclusions: {company_exclusions}")

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

    company_excluded = 0
    if company_exclusions:
        before = len(normalized)
        normalized = [
            job for job in normalized
            if not any(word in job.company.lower() for word in company_exclusions)
        ]
        company_excluded = before - len(normalized)
        log.info(f"Company exclusion filter removed {company_excluded} jobs, {len(normalized)} remaining")

    # Split rather than drop: a job already captured under some other criteria/run
    # still needs to be scored and linked against *this* criteria — it's just not
    # re-enriched or re-uploaded, since its blob (and enrichment) already exist.
    new_jobs, already_captured = [], []
    for job in normalized:
        (already_captured if job_exists(job, customer_id) else new_jobs).append(job)
    log.info(f"Dedup: {len(already_captured)} already in Azure, {len(new_jobs)} new jobs to process")

    if args.enrich:
        log.info("Enriching job descriptions via Claude...")
        client = anthropic.Anthropic(api_key=os.environ.get("ANTHROPIC_API_KEY"))
        new_jobs = [enrich_job(job, client) for job in new_jobs]
        log.info("Enrichment complete")

    log.info("Scoring new jobs against criteria...")
    for job in new_jobs:
        job.criteria_match_score = _calculate_match_score(job, keywords, technology, experience_in_years)

    # Reload each already-captured job's existing (already-enriched) blob so it can
    # be scored against this criteria without re-paying for enrichment.
    log.info("Scoring previously-captured jobs against this criteria...")
    already_captured_scored = []
    for job in already_captured:
        existing = load_job(job, customer_id)
        if existing is None:
            log.warning(f"Expected existing blob for {job.source}:{job.id} but it was missing — skipping link for this run")
            continue
        existing.criteria_match_score = _calculate_match_score(existing, keywords, technology, experience_in_years)
        already_captured_scored.append(existing)

    if args.what_if:
        log.info("[WHAT-IF] Dry run — skipping Azure writes and email")
        for job in new_jobs + already_captured_scored:
            log.info(f"  [{job.source}] {job.title} @ {job.company} | {job.location} | {job.salary}")
        _print_summary(scrape_counts, title_excluded, company_excluded, len(already_captured), saved=0, linked=0, what_if=True)
        return

    log.info("Saving new jobs to Azure Blob Storage...")
    saved, save_skipped = 0, 0
    for job in new_jobs:
        if save_job(job, customer_id):
            upsert_job(job.id, blob_file_name(job), customer_id, args.criteria_id, job.criteria_match_score, job.years_experience)
            saved += 1
        else:
            save_skipped += 1
    log.info(f"Saved {saved} new jobs, skipped {save_skipped} (raced with another save)")

    log.info("Linking previously-captured jobs to this criteria...")
    for job in already_captured_scored:
        upsert_job(job.id, blob_file_name(job), customer_id, args.criteria_id, job.criteria_match_score, job.years_experience)
    log.info(f"Linked {len(already_captured_scored)} previously-captured jobs to this criteria")

    all_relevant_jobs = new_jobs + already_captured_scored
    if args.email:
        log.info(f"Sending results email to {args.email}...")
        send_results_email(all_relevant_jobs, args.email)
    else:
        log.info("No --email provided — skipping email")

    _print_summary(scrape_counts, title_excluded, company_excluded, len(already_captured), saved=saved, linked=len(already_captured_scored))


if __name__ == "__main__":
    main()
