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
}

interface CriteriaJobsGroup {
  criteriaId: string;
  criteriaName: string;
  jobs: JobResult[];
}

const PAGE_SIZE = 10;

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

  get criteriaOptions(): { id: string; name: string }[] {
    return this.groups.map((g) => ({ id: g.criteriaId, name: g.criteriaName }));
  }

  get filteredJobs(): JobResult[] {
    const groups =
      this.selectedCriteriaId === 'all'
        ? this.groups
        : this.groups.filter((g) => g.criteriaId === this.selectedCriteriaId);
    return groups.flatMap((g) => g.jobs);
  }

  get visibleJobs(): JobResult[] {
    return this.filteredJobs.slice(0, this.visibleCount);
  }

  get hasMore(): boolean {
    return this.visibleCount < this.filteredJobs.length;
  }

  onFilter(criteriaId: string): void {
    this.selectedCriteriaId = criteriaId;
    this.visibleCount = PAGE_SIZE;
  }

  loadMore(): void {
    this.visibleCount += PAGE_SIZE;
  }
}
