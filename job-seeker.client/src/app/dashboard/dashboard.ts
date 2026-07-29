import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { FormBuilder, Validators } from '@angular/forms';
import { AuthService } from '../auth';

declare var bootstrap: any;

interface CustomerInfo {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
}

interface CriteriaItem {
  id: string;
  name: string;
}

interface CriteriaResponse {
  criteria: CriteriaItem[];
}

interface ScrapeExecutionItem {
  scrapeId: string;
  status: 'Running' | 'Succeeded' | 'Failed';
  startedAt: string;
  endedAt: string | null;
  errorMessage: string | null;
}

interface ScrapeStatusResponse {
  executions: ScrapeExecutionItem[];
  canScrapeNow: boolean;
}

@Component({
  selector: 'app-dashboard',
  standalone: false,
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.css',
})
export class Dashboard implements OnInit {
  customer: CustomerInfo | null = null;
  loading = true;

  criteria: CriteriaItem[] = [];
  isScraping = false;
  toastMessage = '';
  executions: ScrapeExecutionItem[] = [];
  canScrapeNow = true;

  @ViewChild('successToast') successToastRef!: ElementRef;
  @ViewChild('errorToast') errorToastRef!: ElementRef;

  scrapeForm;

  constructor(
    private authService: AuthService,
    private http: HttpClient,
    private router: Router,
    private fb: FormBuilder
  ) {
    this.scrapeForm = this.fb.group({
      scrapeIndeed: [false],
      scrapeLinkedin: [false],
      criteriaId: ['', Validators.required],
    });
  }

  ngOnInit(): void {
    const token = this.authService.getToken();
    if (!token) {
      this.router.navigate(['/sign-in']);
      return;
    }

    const headers = new HttpHeaders({ Authorization: `Bearer ${token}` });
    this.http.get<CustomerInfo>('/customer/me', { headers }).subscribe({
      next: (c) => {
        this.customer = c;
        this.loading = false;
      },
      error: () => {
        this.authService.logout();
        this.router.navigate(['/sign-in']);
      },
    });

    this.http.get<CriteriaResponse>('/job-seeker/slim-criteria', { headers }).subscribe({
      next: (r) => {
        this.criteria = r.criteria ?? [];
      },
      error: () => {
        this.criteria = [];
      },
    });

    this.fetchScrapeStatus();
  }

  atLeastOneSourceSelected(): boolean {
    return !!(this.scrapeForm.value.scrapeIndeed || this.scrapeForm.value.scrapeLinkedin);
  }

  onScrape(): void {
    if (this.scrapeForm.invalid || !this.atLeastOneSourceSelected() || this.isScraping || !this.canScrapeNow) {
      return;
    }

    const token = this.authService.getToken();
    if (!token) {
      this.router.navigate(['/sign-in']);
      return;
    }

    this.isScraping = true;
    const headers = new HttpHeaders({ Authorization: `Bearer ${token}` });

    this.http.post('/job-seeker/scrape', this.scrapeForm.value, { headers }).subscribe({
      next: () => {
        this.isScraping = false;
        this.fetchScrapeStatus();
        this.showToast(this.successToastRef);
      },
      error: (err: HttpErrorResponse) => {
        this.isScraping = false;
        this.toastMessage = err.error?.error ?? 'Failed to start job search. Please try again.';
        this.showToast(this.errorToastRef);
      },
    });
  }

  formatDuration(e: ScrapeExecutionItem): string {
    if (!e.endedAt) return '—';
    const totalSeconds = Math.max(0, Math.round((new Date(e.endedAt).getTime() - new Date(e.startedAt).getTime()) / 1000));
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return minutes > 0 ? `${minutes}m ${seconds}s` : `${seconds}s`;
  }

  private fetchScrapeStatus(): void {
    const token = this.authService.getToken();
    if (!token) return;

    const headers = new HttpHeaders({ Authorization: `Bearer ${token}` });
    this.http.get<ScrapeStatusResponse>('/job-seeker/scrape-status', { headers }).subscribe({
      next: (s) => {
        this.executions = s.executions ?? [];
        this.canScrapeNow = s.canScrapeNow;
      },
      error: () => {
        this.executions = [];
        this.canScrapeNow = true;
      },
    });
  }

  private showToast(ref: ElementRef): void {
    const toast = new bootstrap.Toast(ref.nativeElement, { delay: 4000 });
    toast.show();
  }
}
