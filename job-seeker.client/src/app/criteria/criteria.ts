import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { AuthService } from '../auth';
import { maxChipCountValidator } from './chip-count-validator';

declare var bootstrap: any;

interface CriteriaDetail {
  id: string;
  name: string;
  keywords: string[];
  jobTitleExclusionList: string[];
  companyNameExclusionList: string[];
  location: string;
  technology: string[];
  distance: number | null;
  minSalary: number | null;
  experienceInYears: number | null;
  jobType: string | null;
  hoursOld: number | null;
  timesScraped: number;
  createdDate: string | null;
}

const JOB_TYPES = ['fulltime', 'parttime', 'contract', 'internship'];
const PAGE_SIZE = 4;

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
  pendingDelete: CriteriaDetail | null = null;
  visibleCount = PAGE_SIZE;

  @ViewChild('criteriaFormCard') formCardRef!: ElementRef;
  @ViewChild('deleteModal') deleteModalRef!: ElementRef;

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
      companyNameExclusionList: [[] as string[], [maxChipCountValidator(10)]],
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

  get visibleCriteria(): CriteriaDetail[] {
    return this.criteriaList.slice(0, this.visibleCount);
  }

  get hasMoreCriteria(): boolean {
    return this.visibleCount < this.criteriaList.length;
  }

  showMoreCriteria(): void {
    this.visibleCount += PAGE_SIZE;
  }

  private fetchCriteria(): void {
    this.loading = true;
    this.http.get<CriteriaDetail[]>('/criteria/get-criteria', { headers: this.authHeaders() }).subscribe({
      next: (list) => {
        // Most recently created first; criteria without a createdDate (pre-dates that
        // column) sort last rather than crashing or clustering at the top.
        this.criteriaList = (list ?? []).slice().sort((a, b) => {
          const aTime = a.createdDate ? new Date(a.createdDate).getTime() : 0;
          const bTime = b.createdDate ? new Date(b.createdDate).getTime() : 0;
          return bTime - aTime;
        });
        this.visibleCount = PAGE_SIZE;
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

  // Pre-loads the add form with a copy of an existing criteria's searchable fields —
  // name is left blank so the user must supply a new, unique one, and submitting goes
  // through the normal add flow (creates a brand-new criteria, doesn't touch the source).
  onClone(c: CriteriaDetail): void {
    this.form.patchValue({
      name: '',
      keywords: c.keywords ?? [],
      jobTitleExclusionList: c.jobTitleExclusionList ?? [],
      companyNameExclusionList: c.companyNameExclusionList ?? [],
      location: c.location ?? '',
      technology: c.technology ?? [],
      distance: c.distance,
      experienceInYears: c.experienceInYears,
      hoursOld: c.hoursOld,
    });
    this.form.markAsPristine();
    this.form.markAsUntouched();
    this.errorMessage = '';
    this.formCardRef?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  // Deleting is destructive to more than just the criteria itself — its jobs lose their
  // keyword/experience context and drop out of the My Jobs filter dropdown (see
  // ResolveCriteriaName/AvailableCriteria on the backend) — so this requires confirmation
  // rather than deleting straight from the trash icon.
  onDeleteClick(c: CriteriaDetail): void {
    this.pendingDelete = c;
    bootstrap.Modal.getOrCreateInstance(this.deleteModalRef.nativeElement).show();
  }

  confirmDelete(): void {
    if (!this.pendingDelete) return;
    const id = this.pendingDelete.id;
    this.pendingDelete = null;
    bootstrap.Modal.getOrCreateInstance(this.deleteModalRef.nativeElement).hide();
    this.onDelete(id);
  }

  private onDelete(id: string): void {
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
        this.form.reset({ keywords: [], jobTitleExclusionList: [], companyNameExclusionList: [], technology: [], jobType: '' });
        this.fetchCriteria();
      },
      error: (err: HttpErrorResponse) => {
        this.submitting = false;
        this.errorMessage = err.error?.error ?? 'Failed to add criteria. Please try again.';
      },
    });
  }
}
