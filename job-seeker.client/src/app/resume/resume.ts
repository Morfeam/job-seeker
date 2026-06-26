import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { AuthService } from '../auth';

interface ResumeItem {
  id: string;
  name: string;
  fileName: string;
  uploadDate: string | null;
  associatedCriteriaId: string | null;
  downloadUrl: string;
}

interface CriteriaItem {
  id: string;
  name: string;
}

interface CriteriaResponse {
  criteria: CriteriaItem[];
}

const MAX_FILE_SIZE_BYTES = 5 * 1024 * 1024;

@Component({
  selector: 'app-resume',
  standalone: false,
  templateUrl: './resume.html',
  styleUrl: './resume.css',
})
export class Resume implements OnInit {
  loading = true;
  submitting = false;
  resumeList: ResumeItem[] = [];
  criteriaList: CriteriaItem[] = [];
  errorMessage = '';
  selectedFile: File | null = null;

  form: FormGroup;

  @ViewChild('fileInput') fileInputRef!: ElementRef<HTMLInputElement>;

  constructor(
    private authService: AuthService,
    private http: HttpClient,
    private router: Router,
    private fb: FormBuilder
  ) {
    this.form = this.fb.group({
      name: ['', [Validators.maxLength(100)]],
      associatedCriteriaId: [''],
    });
  }

  ngOnInit(): void {
    const token = this.authService.getToken();
    if (!token) {
      this.router.navigate(['/sign-in']);
      return;
    }
    this.fetchAll();
  }

  get fileTooLarge(): boolean {
    return !!this.selectedFile && this.selectedFile.size > MAX_FILE_SIZE_BYTES;
  }

  private authHeaders(): HttpHeaders {
    return new HttpHeaders({ Authorization: `Bearer ${this.authService.getToken()}` });
  }

  private fetchAll(): void {
    this.loading = true;
    const headers = this.authHeaders();

    forkJoin({
      resumes: this.http.get<ResumeItem[]>('/resume/get-resumes', { headers }),
      criteria: this.http.get<CriteriaResponse>('/job-seeker/slim-criteria', { headers }),
    }).subscribe({
      next: ({ resumes, criteria }) => {
        this.resumeList = resumes ?? [];
        this.criteriaList = criteria.criteria ?? [];
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Failed to load resumes.';
      },
    });
  }

  resolveCriteriaName(criteriaId: string | null): string {
    if (!criteriaId) return 'None';
    return this.criteriaList.find((c) => c.id === criteriaId)?.name ?? 'None';
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
  }

  onSubmit(): void {
    if (!this.selectedFile || this.fileTooLarge || this.submitting) return;
    this.submitting = true;
    this.errorMessage = '';

    const formData = new FormData();
    formData.append('file', this.selectedFile, this.selectedFile.name);
    const name = this.form.value.name?.trim();
    if (name) formData.append('name', name);
    const criteriaId = this.form.value.associatedCriteriaId;
    if (criteriaId) formData.append('associatedCriteriaId', criteriaId);

    this.http.post('/resume/add', formData, { headers: this.authHeaders() }).subscribe({
      next: () => {
        this.submitting = false;
        this.resetForm();
        this.fetchAll();
      },
      error: (err: HttpErrorResponse) => {
        this.submitting = false;
        this.errorMessage = err.error?.error ?? 'Failed to upload resume. Please try again.';
        if (err.status === 409) {
          this.resetForm();
        }
      },
    });
  }

  onDelete(id: string): void {
    const previous = this.resumeList;
    this.resumeList = this.resumeList.filter((r) => r.id !== id);

    this.http.post('/resume/delete', { resumeId: id }, { headers: this.authHeaders() }).subscribe({
      error: () => {
        this.resumeList = previous;
        this.errorMessage = 'Failed to delete resume. Please try again.';
      },
    });
  }

  private resetForm(): void {
    this.form.reset({ name: '', associatedCriteriaId: '' });
    this.selectedFile = null;
    if (this.fileInputRef) {
      this.fileInputRef.nativeElement.value = '';
    }
  }
}
