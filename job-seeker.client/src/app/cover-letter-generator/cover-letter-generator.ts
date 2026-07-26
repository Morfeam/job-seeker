import { Component, OnInit } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { AuthService } from '../auth';

interface JobItem {
  blobFileName: string;
  title: string;
  company: string;
}

interface GetJobsResponse {
  jobs: { job: { blobFileName: string; title: string; company: string } }[];
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

@Component({
  selector: 'app-cover-letter-generator',
  standalone: false,
  templateUrl: './cover-letter-generator.html',
  styleUrl: './cover-letter-generator.css',
})
export class CoverLetterGenerator implements OnInit {
  loading = true;
  submitting = false;
  jobs: JobItem[] = [];
  resumes: ResumeItem[] = [];
  coverLetters: CoverLetterItem[] = [];
  errorMessage = '';

  form: FormGroup;

  constructor(
    private authService: AuthService,
    private http: HttpClient,
    private fb: FormBuilder
  ) {
    this.form = this.fb.group({
      jobBlobFileName: ['', Validators.required],
      resumeId: ['', Validators.required],
      cvEnrichmentStyle: ['None', Validators.required],
    });
  }

  ngOnInit(): void {
    this.fetchAll();
  }

  get hasCoverLetters(): boolean {
    return this.coverLetters.length > 0;
  }

  private authHeaders(): HttpHeaders {
    return new HttpHeaders({ Authorization: `Bearer ${this.authService.getToken()}` });
  }

  private fetchAll(): void {
    this.loading = true;
    const headers = this.authHeaders();

    forkJoin({
      jobsResponse: this.http.get<GetJobsResponse>('/job-seeker/get-jobs?limit=200', { headers }),
      resumes: this.http.get<ResumeItem[]>('/resume/get-resumes', { headers }),
      coverLetters: this.http.get<CoverLetterItem[]>('/cv/cover-letters', { headers }),
    }).subscribe({
      next: ({ jobsResponse, resumes, coverLetters }) => {
        this.jobs = (jobsResponse?.jobs ?? []).map((j) => ({
          blobFileName: j.job.blobFileName,
          title: j.job.title,
          company: j.job.company,
        }));
        this.resumes = resumes ?? [];
        this.coverLetters = coverLetters ?? [];
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Failed to load form data.';
      },
    });
  }

  private fetchCoverLettersOnly(): void {
    this.http.get<CoverLetterItem[]>('/cv/cover-letters', { headers: this.authHeaders() }).subscribe({
      next: (items) => {
        this.coverLetters = items ?? [];
      },
    });
  }

  onGenerate(): void {
    if (this.form.invalid || this.submitting) return;
    this.submitting = true;
    this.errorMessage = '';

    this.http.post('/cv/generate', this.form.value, { headers: this.authHeaders() }).subscribe({
      next: () => {
        this.submitting = false;
        this.fetchCoverLettersOnly();
      },
      error: (err: HttpErrorResponse) => {
        this.submitting = false;
        this.errorMessage = err.error?.error ?? 'Failed to generate cover letter. Please try again.';
      },
    });
  }

  onSetPrime(coverLetterId: string): void {
    this.http.post('/cv/set-prime', { coverLetterId }, { headers: this.authHeaders() }).subscribe({
      next: () => this.fetchCoverLettersOnly(),
      error: () => {
        this.errorMessage = 'Failed to set Prime cover letter.';
      },
    });
  }
}
