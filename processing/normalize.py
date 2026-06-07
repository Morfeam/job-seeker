from dataclasses import dataclass
from datetime import date
import math


@dataclass
class Technology:
    name: str
    years_experience: int | None
    preferred_years_experience: int | None = None
    is_preferred: bool = False


@dataclass
class NormalizedJob:
    id: str | None
    source: str
    title: str
    company: str
    location: str
    is_remote: bool | None
    salary: str | None
    job_type: str | None
    date_posted: str | None
    description: str | None
    job_url: str | None
    company_url: str | None
    company_industry: str | None
    years_experience: int | None
    technologies: list[Technology] | None
    hard_requirements: list[str] | None
    preferred_requirements: list[str] | None
    soft_requirements: list[str] | None
    ai_summary: str | None
    created_dt: str | None
    last_updated_dt: str | None
    formatted_updated_dt: str | None


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


def _str_or_none(value) -> str | None:
    if value is None or _is_nan(value):
        return None
    s = str(value).strip()
    return s if s else None


def _bool_or_none(value) -> bool | None:
    if value is None or _is_nan(value):
        return None
    return bool(value)


def normalize_job(row: dict) -> NormalizedJob:
    raw_source = row.get("site") or ""
    source = _SITE_NAME_MAP.get(str(raw_source).lower(), str(raw_source).lower())

    raw_date = row.get("date_posted")
    date_posted = raw_date.isoformat() if isinstance(raw_date, date) else _str_or_none(raw_date)

    raw_job_type = row.get("job_type")
    job_type = raw_job_type.value if hasattr(raw_job_type, "value") else _str_or_none(raw_job_type)

    return NormalizedJob(
        id=_str_or_none(row.get("id")),
        source=source,
        title=str(row.get("title") or "").strip(),
        company=str(row.get("company") or "").strip(),
        location=str(row.get("location") or "").strip(),
        is_remote=_bool_or_none(row.get("is_remote")),
        salary=_build_salary(row),
        job_type=job_type,
        date_posted=date_posted,
        description=_str_or_none(row.get("description")),
        job_url=_str_or_none(row.get("job_url")),
        company_url=_str_or_none(row.get("company_url")),
        company_industry=_str_or_none(row.get("company_industry")),
        years_experience=None,
        technologies=None,
        hard_requirements=None,
        preferred_requirements=None,
        soft_requirements=None,
        ai_summary=None,
        created_dt=None,
        last_updated_dt=None,
        formatted_updated_dt=None,
    )
