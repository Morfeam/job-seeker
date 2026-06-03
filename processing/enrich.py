import dataclasses
import json
import logging
import re

import anthropic

from processing.normalize import NormalizedJob, Technology

log = logging.getLogger(__name__)

_MODEL = "claude-haiku-4-5-20251001"

_SYSTEM = (
    "You are a job posting analyst. Given a job description, extract structured information "
    "and return it as JSON with exactly these keys:\n\n"
    '- "technologies": list of objects, each with:\n'
    '    - "name": string — the tool, language, framework, or platform\n'
    '    - "years_experience": integer or null — years explicitly required for this specific technology '
    '(e.g. "4+ years working with SQL" → 4; if no years mentioned for this technology → null)\n'
    '- "hard_requirements": list of strings — concrete must-have requirements (overall years of experience, '
    "degrees, certifications, security clearances)\n"
    '- "soft_requirements": list of strings — workplace or lifestyle requirements (relocation, travel '
    "frequency, on-site days per week, work authorization)\n"
    '- "summary": string — 2-3 sentence plain-English summary of the role\n\n'
    "Example technologies entry: "
    '[{"name": "SQL", "years_experience": 4}, {"name": "Azure", "years_experience": null}]\n\n'
    "Return only valid JSON. No markdown fences, no explanation."
)


def _parse_json(text: str) -> dict:
    text = text.strip()
    # Strip markdown fences Claude sometimes adds despite being told not to
    text = re.sub(r"^```[a-z]*\s*", "", text)
    text = re.sub(r"\s*```$", "", text)
    return json.loads(text.strip())


def enrich_job(job: NormalizedJob, client: anthropic.Anthropic) -> NormalizedJob:
    if not job.description:
        return job

    try:
        response = client.messages.create(
            model=_MODEL,
            max_tokens=1024,
            system=_SYSTEM,
            messages=[{"role": "user", "content": f"Job description:\n\n{job.description}"}],
        )
        data = _parse_json(response.content[0].text)

        technologies = [
            Technology(
                name=t.get("name", ""),
                years_experience=t.get("years_experience"),
            )
            for t in (data.get("technologies") or [])
            if t.get("name")
        ]

        return dataclasses.replace(
            job,
            technologies=technologies,
            hard_requirements=data.get("hard_requirements") or [],
            soft_requirements=data.get("soft_requirements") or [],
            ai_summary=data.get("summary"),
        )
    except Exception as e:
        log.warning(f"Enrichment failed for '{job.title}' ({job.id}): {e}")
        return job
