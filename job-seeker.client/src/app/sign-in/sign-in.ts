import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../auth';

@Component({
  selector: 'app-sign-in',
  standalone: false,
  templateUrl: './sign-in.html',
  styleUrl: './sign-in.css',
})
export class SignIn implements OnInit {
  form!: FormGroup;
  errorMessage = '';
  loading = false;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private authService: AuthService,
    private router: Router
  ) {}

  ngOnInit(): void {
    if (this.authService.getToken()) {
      this.router.navigate(['/dashboard']);
      return;
    }
    this.form = this.fb.group({
      email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
      password: ['', Validators.required],
      rememberMe: [false],
    });
  }

  onSubmit(): void {
    if (this.form.invalid) return;
    this.loading = true;
    this.errorMessage = '';

    this.http.post<{ token: string }>('/customer/sign-in', {
      email: this.form.value.email,
      password: this.form.value.password,
    }).subscribe({
      next: (res) => {
        this.authService.login(res.token, this.form.value.rememberMe);
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.status === 429
          ? 'Too many sign-in attempts. Please wait a minute and try again.'
          : err.error?.error ?? 'Sign in failed. Please try again.';
      },
    });
  }
}
