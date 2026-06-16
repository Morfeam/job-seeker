import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { passwordStrengthValidator, passwordMatchValidator } from '../password-validator';

@Component({
  selector: 'app-reset-password',
  standalone: false,
  templateUrl: './reset-password.html',
  styleUrl: './reset-password.css',
})
export class ResetPassword implements OnInit {
  form!: FormGroup;
  errorMessage = '';
  loading = false;
  private token = '';

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private route: ActivatedRoute,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('id') ?? '';
    this.form = this.fb.group(
      {
        password: ['', [Validators.required, passwordStrengthValidator]],
        confirmPassword: ['', Validators.required],
      },
      { validators: passwordMatchValidator }
    );
  }

  onSubmit(): void {
    if (this.form.invalid) return;
    this.loading = true;
    this.errorMessage = '';

    this.http.post('/customer/reset-password', {
      token: this.token,
      password: this.form.value.password,
    }).subscribe({
      next: () => this.router.navigate(['/sign-in']),
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.error ?? 'Reset failed. The link may have expired.';
      },
    });
  }
}
