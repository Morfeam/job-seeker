import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { FormBuilder } from '@angular/forms';
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

interface JobCardResult {
  job: JobResult;
  criteriaId: string;
  criteriaName: string;
  keywords: string[] | null;
  experienceInYears: number | null;
}

interface GetJobsResponse {
  jobs: JobCardResult[];
  availableCriteria: { id: string; name: string }[];
  totalMatched: number;
}

interface JobCard {
  job: JobResult;
  technologies: DisplayTechnology[];
  criteriaName: string;
}

const PAGE_SIZE = 10;
const SKILLS_PREVIEW_COUNT = 10;
const INITIAL_LIMIT = 20;
const SEARCH_LIMIT = 200;

@Component({
  selector: 'app-user-jobs',
  standalone: false,
  templateUrl: './user-jobs.html',
  styleUrl: './user-jobs.css',
})
export class UserJobs implements OnInit {
  loading = true;
  searching = false;
  searchError = '';
  cards: JobCard[] = [];
  availableCriteria: { id: string; name: string }[] = [];
  totalMatched = 0;
  visibleCount = PAGE_SIZE;
  expandedSkills = new Set<number>();

  searchForm;

  constructor(
    private authService: AuthService,
    private http: HttpClient,
    private router: Router,
    private fb: FormBuilder
  ) {
    this.searchForm = this.fb.group({
      criteriaId: ['all'],
      sort: ['recent'],
      matchYearsOfExperience: [true],
    });
  }

  ngOnInit(): void {
    const token = this.authService.getToken();
    if (!token) {
      this.router.navigate(['/sign-in']);
      return;
    }

    this.fetchJobs(INITIAL_LIMIT, true);
  }

  get visibleJobs(): JobCard[] {
    return this.cards.slice(0, this.visibleCount);
  }

  get hasMore(): boolean {
    return this.visibleCount < this.cards.length;
  }

  get isCapped(): boolean {
    return this.totalMatched > this.cards.length;
  }

  onSearch(): void {
    if (this.searching) return;
    this.fetchJobs(SEARCH_LIMIT, false);
  }

  loadMore(): void {
    this.visibleCount += PAGE_SIZE;
  }

  private fetchJobs(limit: number, isInitialLoad: boolean): void {
    const token = this.authService.getToken();
    if (!token) {
      this.router.navigate(['/sign-in']);
      return;
    }

    this.searching = true;
    this.searchError = '';
    const headers = new HttpHeaders({ Authorization: `Bearer ${token}` });
    const { criteriaId, sort, matchYearsOfExperience } = this.searchForm.value;
    const params = new HttpParams()
      .set('criteriaId', criteriaId ?? 'all')
      .set('sort', sort ?? 'recent')
      .set('matchYearsOfExperience', String(matchYearsOfExperience ?? true))
      .set('limit', String(limit));

    this.http.get<GetJobsResponse>('/job-seeker/get-jobs', { headers, params }).subscribe({
      next: (response) => {
        this.availableCriteria = response.availableCriteria ?? [];
        this.totalMatched = response.totalMatched ?? 0;
        this.cards = (response.jobs ?? []).map((j) => {
          const keywords = (j.keywords ?? []).map((k) => k.toLowerCase().trim()).filter(Boolean);
          return {
            job: j.job,
            technologies: this.buildTechnologies(j.job.technologies, keywords),
            criteriaName: j.criteriaName,
          };
        });
        this.visibleCount = PAGE_SIZE;
        this.expandedSkills.clear();
        this.loading = false;
        this.searching = false;
      },
      error: () => {
        this.searching = false;
        if (isInitialLoad) {
          this.router.navigate(['/error']);
        } else {
          this.loading = false;
          this.searchError = 'Failed to search jobs. Please try again.';
        }
      },
    });
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
