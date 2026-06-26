import { Component, Input } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'app-chip-input',
  standalone: false,
  templateUrl: './chip-input.html',
  styleUrl: './chip-input.css',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: ChipInput,
      multi: true,
    },
  ],
})
export class ChipInput implements ControlValueAccessor {
  @Input() placeholder = 'Type and press Enter…';
  @Input() maxItems: number | null = null;

  chips: string[] = [];
  draft = '';
  disabled = false;

  private onChange: (value: string[]) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: string[] | null): void {
    this.chips = value ?? [];
  }

  registerOnChange(fn: (value: string[]) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }

  get atMax(): boolean {
    return this.maxItems !== null && this.chips.length >= this.maxItems;
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ',') {
      event.preventDefault();
      this.commitDraft();
    } else if (event.key === 'Backspace' && !this.draft && this.chips.length) {
      this.removeChip(this.chips.length - 1);
    }
  }

  commitDraft(): void {
    const value = this.draft.trim();
    this.draft = '';
    if (!value || this.atMax) return;
    if (this.chips.some((c) => c.toLowerCase() === value.toLowerCase())) return;

    this.chips = [...this.chips, value];
    this.onChange(this.chips);
    this.onTouched();
  }

  removeChip(index: number): void {
    this.chips = this.chips.filter((_, i) => i !== index);
    this.onChange(this.chips);
    this.onTouched();
  }
}
