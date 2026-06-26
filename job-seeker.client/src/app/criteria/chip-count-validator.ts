import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

export const maxChipCountValidator =
  (max: number): ValidatorFn =>
  (control: AbstractControl): ValidationErrors | null => {
    const value: string[] = control.value ?? [];
    return value.length > max ? { maxChipCount: `Maximum ${max} entries allowed.` } : null;
  };
