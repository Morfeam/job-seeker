import os
import logging

import resend

from processing.normalize import NormalizedJob

log = logging.getLogger(__name__)

FROM_ADDRESS = "Job Seeker <no-reply@joeprints.shop>"
SUBJECT = "Daily Job Search Results"

_RESEND_API_KEY = ""  # placeholder — set RESEND_API_KEY env var


def _get_api_key() -> str:
    return os.environ.get("RESEND_API_KEY") or _RESEND_API_KEY


def _build_job_card(job: NormalizedJob) -> str:
    salary_display = job.salary if job.salary else "Salary not listed"
    icon_block = (
        f'<div style="width:100px;height:100px;background:#e0e0e0;'
        f'display:flex;align-items:center;justify-content:center;'
        f'font-size:11px;color:#888;text-align:center;flex-shrink:0;">'
        f"<!-- SOURCE ICON: {job.source} -->"
        f"Icon TBD"
        f"</div>"
    )
    return f"""
<div style="display:flex;align-items:flex-start;gap:16px;border:1px solid #ddd;
            border-radius:8px;padding:16px;margin:12px 0;font-family:sans-serif;
            max-width:600px;">
    {icon_block}
    <div>
        <h3 style="margin:0 0 4px">{job.title}</h3>
        <p style="margin:2px 0;color:#555;">{job.company}</p>
        <p style="margin:2px 0;color:#777;">{job.location}</p>
        <p style="margin:2px 0;color:#444;">{salary_display}</p>
        <p style="margin:4px 0;font-size:11px;color:#aaa;text-transform:uppercase;">
            {job.source}
        </p>
    </div>
</div>"""


def send_results_email(jobs: list[NormalizedJob], to_address: str) -> None:
    top5 = jobs[:5]
    cards_html = "".join(_build_job_card(j) for j in top5)
    html_body = f"""<!DOCTYPE html>
<html>
<body style="font-family:sans-serif;max-width:640px;margin:0 auto;padding:24px;">
    <h2 style="color:#222;">Daily Job Search Results</h2>
    <p style="color:#555;">{len(top5)} job{'' if len(top5) == 1 else 's'} found.</p>
    {cards_html}
    <hr style="margin-top:32px;border:none;border-top:1px solid #eee;">
    <p style="font-size:11px;color:#aaa;">Sent by Job Seeker · no-reply@joeprints.shop</p>
</body>
</html>"""

    resend.api_key = _get_api_key()
    response = resend.Emails.send({
        "from": FROM_ADDRESS,
        "to": [to_address],
        "subject": SUBJECT,
        "html": html_body,
    })
    log.info(f"Email sent — id: {response.get('id', 'unknown')}")
