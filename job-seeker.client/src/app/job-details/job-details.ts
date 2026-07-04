import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { AuthService } from '../auth';

interface JobTechnology {
  name: string;
  yearsExperience: number | null;
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
  criteriaMatchScore: number | null;
  blobFileName: string | null;
}

interface ResumeItem {
  id: string;
  name: string;
}

interface CoverLetterItem {
  coverLetterId: string;
  coverLetterName: string;
  coverLetterFileName: string;
  relatedResumeId: string;
  relatedResumeName: string;
  isPrime: boolean;
  blobDownloadURL: string;
  createdDate: string | null;
}

const ALLOWED_STYLES = ['None', 'Prime', 'All'];

@Component({
  selector: 'app-job-details',
  standalone: false,
  templateUrl: './job-details.html',
  styleUrl: './job-details.css',
})
export class JobDetails implements OnInit {
  loading = true;
  submitting = false;
  job: JobResult | null = null;
  blobFileName = '';
  resumes: ResumeItem[] = [];
  hasCoverLetters = false;
  latestCoverLetter: CoverLetterItem | null = null;
  coverLetterText: string | null = null;
  loadingCoverLetter = false;
  errorMessage = '';
  generateSuccess = false;
  settingPrime = false;
  allowedStyles = ALLOWED_STYLES;

  form: FormGroup;

  constructor(
    private authService: AuthService,
    private http: HttpClient,
    private router: Router,
    private route: ActivatedRoute,
    private fb: FormBuilder
  ) {
    this.form = this.fb.group({
      resumeId: ['', Validators.required],
      cvEnrichmentStyle: ['None', Validators.required],
    });
  }

  ngOnInit(): void {
    const token = this.authService.getToken();
    if (!token) {
      this.router.navigate(['/sign-in']);
      return;
    }

    this.blobFileName = this.route.snapshot.queryParamMap.get('job') ?? '';
    if (!this.blobFileName) {
      this.router.navigate(['/my-jobs']);
      return;
    }

    const headers = this.authHeaders();
    forkJoin({
      job: this.http.get<JobResult>(`/job-seeker/get-job?blobFileName=${encodeURIComponent(this.blobFileName)}`, { headers }),
      resumes: this.http.get<ResumeItem[]>('/resume/get-resumes', { headers }),
      coverLetters: this.http.get<CoverLetterItem[]>('/cv/cover-letters', { headers }),
    }).subscribe({
      next: ({ job, resumes, coverLetters }) => {
        this.job = job;
        this.resumes = resumes ?? [];
        this.hasCoverLetters = (coverLetters ?? []).length > 0;
        this.loading = false;
        if (job.id) {
          this.fetchLatestCoverLetter(job.id);
        }
      },
      error: () => {
        this.router.navigate(['/error']);
      },
    });
  }

  private authHeaders(): HttpHeaders {
    return new HttpHeaders({ Authorization: `Bearer ${this.authService.getToken()}` });
  }

  private fetchLatestCoverLetter(jobId: string): void {
    this.loadingCoverLetter = true;
    this.latestCoverLetter = null;
    this.coverLetterText = null;

    this.http
      .get<CoverLetterItem>(`/cv/latest-for-job?jobId=${encodeURIComponent(jobId)}`, { headers: this.authHeaders() })
      .subscribe({
        next: (cl) => {
          this.latestCoverLetter = cl;
          // Fetch the actual text content.
          this.http
            .get<{ text: string }>(`/cv/cover-letter-text/${cl.coverLetterId}`, { headers: this.authHeaders() })
            .subscribe({
              next: (r) => {
                this.coverLetterText = r.text;
                this.loadingCoverLetter = false;
              },
              error: () => {
                this.loadingCoverLetter = false;
              },
            });
        },
        error: () => {
          // 404 means no cover letter exists for this job yet — that's fine.
          this.loadingCoverLetter = false;
        },
      });
  }

  onSetPrime(coverLetterId: string): void {
    this.settingPrime = true;
    this.http.post('/cv/set-prime', { coverLetterId }, { headers: this.authHeaders() }).subscribe({
      next: () => {
        this.settingPrime = false;
        if (this.latestCoverLetter) {
          this.latestCoverLetter = { ...this.latestCoverLetter, isPrime: true };
        }
      },
      error: () => {
        this.settingPrime = false;
      },
    });
  }

  onGenerate(): void {
    if (this.form.invalid || this.submitting) return;
    this.submitting = true;
    this.errorMessage = '';
    this.generateSuccess = false;

    const payload = {
      resumeId: this.form.value.resumeId,
      jobBlobFileName: this.blobFileName,
      cvEnrichmentStyle: this.form.value.cvEnrichmentStyle,
    };

    this.http.post('/cv/generate', payload, { headers: this.authHeaders() }).subscribe({
      next: () => {
        this.submitting = false;
        this.generateSuccess = true;
        this.form.reset({ resumeId: '', cvEnrichmentStyle: 'None' });
        if (this.job?.id) {
          this.fetchLatestCoverLetter(this.job.id);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.submitting = false;
        this.errorMessage = err.error?.error ?? 'Failed to generate cover letter. Please try again.';
      },
    });
  }
}
