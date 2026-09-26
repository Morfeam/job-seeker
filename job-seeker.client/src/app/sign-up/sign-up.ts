import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../auth';
import { passwordStrengthValidator, passwordMatchValidator } from '../password-validator';
import { TERMS_VERSION } from '../terms/terms';

@Component({
  selector: 'app-sign-up',
  standalone: false,
  templateUrl: './sign-up.html',
  styleUrl: './sign-up.css',
})
export class SignUp implements OnInit {
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
    this.form = this.fb.group(
      {
        firstName: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(50)]],
        lastName: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(50)]],
        email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
        password: ['', [Validators.required, passwordStrengthValidator]],
        confirmPassword: ['', Validators.required],
        acceptTerms: [false, Validators.requiredTrue],
        rememberMe: [false],
      },
      { validators: passwordMatchValidator }
    );
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.loading = true;
    this.errorMessage = '';

    this.http.post<{ token: string }>('/customer/sign-up', {
      firstName: this.form.value.firstName,
      lastName: this.form.value.lastName,
      email: this.form.value.email,
      password: this.form.value.password,
      acceptedTerms: this.form.value.acceptTerms,
      termsVersion: TERMS_VERSION,
    }).subscribe({
      next: (res) => {
        this.authService.login(res.token, this.form.value.rememberMe);
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.error ?? 'Sign up failed. Please try again.';
      },
    });
  }
}
