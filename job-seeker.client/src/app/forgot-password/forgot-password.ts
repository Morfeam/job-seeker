import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpClient } from '@angular/common/http';

@Component({
  selector: 'app-forgot-password',
  standalone: false,
  templateUrl: './forgot-password.html',
  styleUrl: './forgot-password.css',
})
export class ForgotPassword implements OnInit {
  form!: FormGroup;
  successMessage = '';
  errorMessage = '';
  loading = false;

  constructor(private fb: FormBuilder, private http: HttpClient) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    });
  }

  onSubmit(): void {
    if (this.form.invalid) return;
    this.loading = true;
    this.successMessage = '';
    this.errorMessage = '';

    this.http.post('/customer/forgot-password', { email: this.form.value.email }).subscribe({
      next: () => {
        this.loading = false;
        this.successMessage = 'Reset link sent! Check your inbox.';
        this.form.reset();
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.status === 429
          ? 'Too many requests. Please wait a minute and try again.'
          : 'Something went wrong. Please try again.';
      },
    });
  }
}
