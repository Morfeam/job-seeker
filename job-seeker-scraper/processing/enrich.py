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
    "IMPORTANT: If the language is ambiguous or unclear about whether something is required or preferred, "
    "treat it as a hard requirement. Only mark something as preferred when the posting explicitly uses "
    'language like "nice to have", "preferred", "a plus", "ideally", or "not required".\n\n'
    '- "technologies": list of objects, each with:\n'
    '    - "name": string — the tool, language, framework, or platform\n'
    '    - "is_preferred": boolean — true ONLY when explicitly marked as optional/nice-to-have; default false\n'
    '    - "years_experience": integer or null — years explicitly required for this technology '
    '(e.g. "must have 4+ years with SQL" -> 4; null if not mentioned or not required)\n'
    '    - "preferred_years_experience": integer or null — years mentioned as preferred but not mandatory '
    '(e.g. "ideally 5+ years with React" -> 5; null if not mentioned)\n'
    '- "hard_requirements": list of strings — must-have requirements (years of experience, degrees, '
    "certifications, clearances). When in doubt, list here rather than preferred_requirements.\n"
    '- "preferred_requirements": list of strings — qualifications EXPLICITLY framed as optional. '
    "Leave empty if nothing clearly qualifies.\n"
    '- "soft_requirements": list of strings — workplace or lifestyle requirements (relocation, travel '
    "frequency, on-site days per week, work authorization)\n"
    '- "summary": string — 2-3 sentence plain-English summary of the role\n\n'
    "Example technologies entry: "
    '[{"name": "SQL", "is_preferred": false, "years_experience": 4, "preferred_years_experience": null}, '
    '{"name": "Kubernetes", "is_preferred": true, "years_experience": null, "preferred_years_experience": 2}]\n\n'
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
            max_tokens=2048,
            system=_SYSTEM,
            messages=[{"role": "user", "content": f"Job description:\n\n{job.description}"}],
        )
        data = _parse_json(response.content[0].text)

        technologies = [
            Technology(
                name=t.get("name", ""),
                years_experience=t.get("years_experience"),
                preferred_years_experience=t.get("preferred_years_experience"),
                is_preferred=t.get("is_preferred", False),
            )
            for t in (data.get("technologies") or [])
            if t.get("name")
        ]

        return dataclasses.replace(
            job,
            technologies=technologies,
            hard_requirements=data.get("hard_requirements") or [],
            preferred_requirements=data.get("preferred_requirements") or [],
            soft_requirements=data.get("soft_requirements") or [],
            ai_summary=data.get("summary"),
        )
    except Exception as e:
        log.warning(f"Enrichment failed for '{job.title}' ({job.id}): {e}")
        return job
