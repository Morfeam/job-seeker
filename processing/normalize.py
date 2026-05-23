from dataclasses import dataclass
import math


@dataclass
class NormalizedJob:
    source: str
    title: str
    company: str
    location: str
    salary: str | None
    years_experience: int | None


def _is_nan(value) -> bool:
    try:
        return math.isnan(value)
    except (TypeError, ValueError):
        return False


def _build_salary(row: dict) -> str | None:
    min_amt = row.get("min_amount")
    max_amt = row.get("max_amount")
    currency = row.get("currency") or ""
    interval = row.get("interval") or ""

    has_min = min_amt is not None and not _is_nan(min_amt)
    has_max = max_amt is not None and not _is_nan(max_amt)

    if not has_min and not has_max:
        return None

    symbol = "$" if currency == "USD" else (currency + " " if currency else "")
    parts = []
    if has_min:
        parts.append(f"{symbol}{int(min_amt):,}")
    if has_max:
        parts.append(f"{symbol}{int(max_amt):,}")

    result = " - ".join(parts)
    if interval:
        result += f"/{interval}"
    return result


_SITE_NAME_MAP = {
    "zip_recruiter": "ziprecruiter",
    "indeed": "indeed",
    "linkedin": "linkedin",
}


def normalize_job(row: dict) -> NormalizedJob:
    raw_source = row.get("site") or ""
    source = _SITE_NAME_MAP.get(str(raw_source).lower(), str(raw_source).lower())

    title = str(row.get("title") or "").strip()
    company = str(row.get("company") or "").strip()
    location = str(row.get("location") or "").strip()

    return NormalizedJob(
        source=source,
        title=title,
        company=company,
        location=location,
        salary=_build_salary(row),
        years_experience=None,
    )
