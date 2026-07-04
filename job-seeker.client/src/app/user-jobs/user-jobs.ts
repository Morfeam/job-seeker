import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { AuthService } from '../auth';

interface JobTechnology {
  name: string;
  yearsExperience: number | null;
  preferredYearsExperience: number | null;
  isPreferred: boolean;
}

interface DisplayTechnology extends JobTechnology {
  matched: boolean;
}

interface JobResult {
  id: string | null;
  source: string | null;
  title: string | null;
  company: string | null;
  location: string | null;
  isRemote: boolean | null;
  salary: string | null;
  jobType: string | null;
  datePosted: string | null;
  description: string | null;
  jobUrl: string | null;
  companyUrl: string | null;
  companyIndustry: string | null;
  yearsExperience: number | null;
  technologies: JobTechnology[] | null;
  hardRequirements: string[] | null;
  preferredRequirements: string[] | null;
  softRequirements: string[] | null;
  aiSummary: string | null;
  createdDt: string | null;
  lastUpdatedDt: string | null;
  formattedUpdatedDt: string | null;
  criteriaMatchScore: number | null;
  blobFileName: string | null;
}

interface CriteriaJobsGroup {
  criteriaId: string;
  criteriaName: string;
  keywords: string[] | null;
  jobs: JobResult[];
}

interface JobCard {
  job: JobResult;
  technologies: DisplayTechnology[];
  criteriaName: string;
}

const PAGE_SIZE = 10;
const SKILLS_PREVIEW_COUNT = 10;

@Component({
  selector: 'app-user-jobs',
  standalone: false,
  templateUrl: './user-jobs.html',
  styleUrl: './user-jobs.css',
})
export class UserJobs implements OnInit {
  loading = true;
  groups: CriteriaJobsGroup[] = [];
  selectedCriteriaId = 'all';
  sortByScore = true;
  visibleCount = PAGE_SIZE;

  constructor(
    private authService: AuthService,
    private http: HttpClient,
    private router: Router
  ) {}

  ngOnInit(): void {
    const token = this.authService.getToken();
    if (!token) {
      this.router.navigate(['/sign-in']);
      return;
    }

    const headers = new HttpHeaders({ Authorization: `Bearer ${token}` });
    this.http.get<CriteriaJobsGroup[]>('/job-seeker/get-jobs', { headers }).subscribe({
      next: (groups) => {
        this.groups = groups ?? [];
        this.loading = false;
      },
      error: () => {
        this.router.navigate(['/error']);
      },
    });
  }

  expandedSkills = new Set<number>();

  get criteriaOptions(): { id: string; name: string }[] {
    return this.groups.map((g) => ({ id: g.criteriaId, name: g.criteriaName }));
  }

  get filteredJobs(): JobCard[] {
    const groups =
      this.selectedCriteriaId === 'all'
        ? this.groups
        : this.groups.filter((g) => g.criteriaId === this.selectedCriteriaId);
    const cards = groups.flatMap((g) => {
      const keywords = (g.keywords ?? []).map((k) => k.toLowerCase().trim()).filter(Boolean);
      return g.jobs.map((job) => ({
        job,
        technologies: this.buildTechnologies(job.technologies, keywords),
        criteriaName: g.criteriaName,
      }));
    });
    if (this.sortByScore) {
      return cards.sort((a, b) => (b.job.criteriaMatchScore ?? 0) - (a.job.criteriaMatchScore ?? 0));
    }
    return cards;
  }

  get visibleJobs(): JobCard[] {
    return this.filteredJobs.slice(0, this.visibleCount);
  }

  get hasMore(): boolean {
    return this.visibleCount < this.filteredJobs.length;
  }

  onFilter(criteriaId: string): void {
    this.selectedCriteriaId = criteriaId;
    this.visibleCount = PAGE_SIZE;
    this.expandedSkills.clear();
  }

  onSortByScore(checked: boolean): void {
    this.sortByScore = checked;
    this.visibleCount = PAGE_SIZE;
    this.expandedSkills.clear();
  }

  loadMore(): void {
    this.visibleCount += PAGE_SIZE;
  }

  visibleTechnologies(card: JobCard, index: number): DisplayTechnology[] {
    return this.expandedSkills.has(index)
      ? card.technologies
      : card.technologies.slice(0, SKILLS_PREVIEW_COUNT);
  }

  hasMoreSkills(card: JobCard): boolean {
    return card.technologies.length > SKILLS_PREVIEW_COUNT;
  }

  isSkillsExpanded(index: number): boolean {
    return this.expandedSkills.has(index);
  }

  toggleSkills(index: number): void {
    if (this.expandedSkills.has(index)) {
      this.expandedSkills.delete(index);
    } else {
      this.expandedSkills.add(index);
    }
  }

  private buildTechnologies(
    technologies: JobTechnology[] | null,
    keywords: string[]
  ): DisplayTechnology[] {
    return (technologies ?? [])
      .map((tech) => ({ ...tech, matched: this.isKeywordMatch(tech.name, keywords) }))
      .sort((a, b) => Number(b.matched) - Number(a.matched));
  }

  private isKeywordMatch(name: string, keywords: string[]): boolean {
    const lower = (name ?? '').toLowerCase().trim();
    if (!lower) return false;
    return keywords.some((kw) => kw === lower || lower.includes(kw) || kw.includes(lower));
  }
}
