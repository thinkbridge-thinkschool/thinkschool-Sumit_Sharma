import { Component, ElementRef, computed, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthSession } from '../auth/auth-session';

function notBlank(control: FormControl<string>): ValidationErrors | null {
  return control.value.trim().length === 0 ? { blank: true } : null;
}

interface LoginForm {
  displayName: FormControl<string>;
}

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  private readonly authSession = inject(AuthSession);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);

  private readonly nameInput = viewChild<ElementRef<HTMLInputElement>>('nameInput');

  private readonly queryParamMap = toSignal(this.route.queryParamMap, { requireSync: true });
  readonly redirectTo = computed(() => this.queryParamMap().get('redirectTo') ?? '/quotes');

  readonly form: FormGroup<LoginForm> = this.fb.group({
    displayName: this.fb.control('', [Validators.required, notBlank, Validators.maxLength(80)]),
  });

  readonly signingIn = this.authSession.signingIn;
  readonly error = this.authSession.error;

  get displayName(): FormControl<string> {
    return this.form.controls.displayName;
  }

  submit(): void {
    if (this.signingIn()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.nameInput()?.nativeElement.focus();
      return;
    }

    const { displayName } = this.form.getRawValue();

    this.authSession.signIn(displayName).subscribe((success) => {
      if (success) {
        this.router.navigateByUrl(this.redirectTo());
      }
    });
  }
}
