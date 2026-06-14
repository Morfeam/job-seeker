import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

export const passwordStrengthValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value: string = control.value ?? '';
  if (!value) return null;

  if (value.length < 8 || value.length > 29) {
    return { passwordStrength: 'Password must be between 8 and 29 characters.' };
  }
  if (!/[A-Z]/.test(value)) {
    return { passwordStrength: 'Password must contain at least one uppercase letter.' };
  }
  if (!/[^a-zA-Z0-9]/.test(value)) {
    return { passwordStrength: 'Password must contain at least one special character.' };
  }
  return null;
};

export const passwordMatchValidator: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const password = group.get('password')?.value;
  const confirm = group.get('confirmPassword')?.value;
  return password === confirm ? null : { passwordMismatch: true };
};
