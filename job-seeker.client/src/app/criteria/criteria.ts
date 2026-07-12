import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { AuthService } from '../auth';
import { maxChipCountValidator } from './chip-count-validator';

interface CriteriaDetail {
  id: string;
  name: string;
  keywords: string[];
  jobTitleExclusionList: string[];
  location: string;
  technology: string[];
  distance: number | null;
  minSalary: number | null;
  experienceInYears: number | null;
  jobType: string | null;
  hoursOld: number | null;
}

const JOB_TYPES = ['fulltime', 'parttime', 'contract', 'internship'];

const HOURS_OLD_PRESETS = [
  { hours: 24, label: '1 day' },
  { hours: 168, label: '1 week' },
  { hours: 336, label: '2 weeks' },
];

@Component({
  selector: 'app-criteria',
  standalone: false,
  templateUrl: './criteria.html',
  styleUrl: './criteria.css',
})
export class Criteria implements OnInit {
  loading = true;
  submitting = false;
  criteriaList: CriteriaDetail[] = [];
  errorMessage = '';
  jobTypes = JOB_TYPES;
  hoursOldPresets = HOURS_OLD_PRESETS;

  form: FormGroup;

  constructor(
    private authService: AuthService,
    private http: HttpClient,
    private router: Router,
    private fb: FormBuilder
  ) {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(50)]],
      keywords: [[] as string[], [maxChipCountValidator(5)]],
      jobTitleExclusionList: [[] as string[], [maxChipCountValidator(10)]],
      location: ['', Validators.required],
      technology: [[] as string[]],
      distance: [null, [Validators.min(1), Validators.max(50)]],
      experienceInYears: [null, [Validators.min(0), Validators.max(50)]],
      // Disabled for now — these don't affect the scrape or My Jobs matching yet,
      // so the form just submits their empty/default values.
      minSalary: [{ value: null, disabled: true }],
      jobType: [{ value: '', disabled: true }],
      hoursOld: [null],
    });
  }

  ngOnInit(): void {
    const token = this.authService.getToken();
    if (!token) {
      this.router.navigate(['/sign-in']);
      return;
    }
    this.fetchCriteria();
  }

  private authHeaders(): HttpHeaders {
    return new HttpHeaders({ Authorization: `Bearer ${this.authService.getToken()}` });
  }

  private fetchCriteria(): void {
    this.loading = true;
    this.http.get<CriteriaDetail[]>('/criteria/get-criteria', { headers: this.authHeaders() }).subscribe({
      next: (list) => {
        this.criteriaList = list ?? [];
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Failed to load criteria.';
      },
    });
  }

  setHoursOld(hours: number): void {
    this.form.get('hoursOld')?.setValue(hours);
  }

  onDelete(id: string): void {
    const previous = this.criteriaList;
    this.criteriaList = this.criteriaList.filter((c) => c.id !== id);

    this.http.post('/criteria/delete', { criteriaId: id }, { headers: this.authHeaders() }).subscribe({
      error: () => {
        this.criteriaList = previous;
        this.errorMessage = 'Failed to delete criteria. Please try again.';
      },
    });
  }

  onSubmit(): void {
    if (this.form.invalid || this.submitting) return;
    this.submitting = true;
    this.errorMessage = '';

    this.http.post('/criteria/add', this.form.value, { headers: this.authHeaders() }).subscribe({
      next: () => {
        this.submitting = false;
        this.form.reset({ keywords: [], jobTitleExclusionList: [], technology: [], jobType: '' });
        this.fetchCriteria();
      },
      error: (err: HttpErrorResponse) => {
        this.submitting = false;
        this.errorMessage = err.error?.error ?? 'Failed to add criteria. Please try again.';
      },
    });
  }
}
