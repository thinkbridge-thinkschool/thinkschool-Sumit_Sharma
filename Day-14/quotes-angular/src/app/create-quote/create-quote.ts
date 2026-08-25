import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { QuotesApi } from '../services/quotes-api';
import { Quote, QuoteValidationProblem } from '../models/quote.model';

function notBlank(control: FormControl<string>): ValidationErrors | null {
  return control.value.trim().length === 0 ? { blank: true } : null;
}

interface CreateQuoteForm {
  author: FormControl<string>;
  text: FormControl<string>;
}

@Component({
  selector: 'app-create-quote',
  imports: [ReactiveFormsModule],
  templateUrl: './create-quote.html',
  styleUrl: './create-quote.css',
})
export class CreateQuote {
  private readonly quotesApi = inject(QuotesApi);
  private readonly fb = inject(NonNullableFormBuilder);

  private readonly authorInput = viewChild<ElementRef<HTMLInputElement>>('authorInput');
  private readonly textInput = viewChild<ElementRef<HTMLTextAreaElement>>('textInput');

  readonly form: FormGroup<CreateQuoteForm> = this.fb.group({
    author: this.fb.control('', [Validators.required, notBlank, Validators.maxLength(200)]),
    text: this.fb.control('', [Validators.required, notBlank, Validators.maxLength(1000)]),
  });

  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly createdQuote = signal<Quote | null>(null);

  get author(): FormControl<string> {
    return this.form.controls.author;
  }

  get text(): FormControl<string> {
    return this.form.controls.text;
  }

  submit(): void {
    if (this.submitting()) {
      return;
    }

    this.submitError.set(null);
    this.createdQuote.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalidControl();
      return;
    }

    this.submitting.set(true);

    const { author, text } = this.form.getRawValue();

    this.quotesApi.createQuote({ author, text }).subscribe({
      next: (quote) => {
        this.submitting.set(false);
        this.createdQuote.set(quote);
        this.form.reset({ author: '', text: '' });
        this.authorInput()?.nativeElement.focus();
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.submitError.set(this.describeError(err));
      },
    });
  }

  private focusFirstInvalidControl(): void {
    if (this.author.invalid) {
      this.authorInput()?.nativeElement.focus();
      return;
    }

    if (this.text.invalid) {
      this.textInput()?.nativeElement.focus();
    }
  }

  private describeError(err: HttpErrorResponse): string {
    if (err.status === 400) {
      const problem = err.error as QuoteValidationProblem | null;
      const messages = Object.values(problem?.errors ?? {}).flat();
      return messages.length > 0
        ? messages.join(' ')
        : 'The Week-1 API rejected this quote. Please check the fields and try again.';
    }

    if (err.status === 401 || err.status === 403) {
      return 'This environment is not authorized to create quotes. Ask a developer to configure an access token with the quotes.write scope for local testing.';
    }

    if (err.status === 0) {
      return 'Could not reach the Week-1 Quotes API. Confirm it is running and try again.';
    }

    return 'The Week-1 Quotes API could not create the quote right now. Please try again.';
  }
}
