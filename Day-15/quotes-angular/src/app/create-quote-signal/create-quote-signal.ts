import { Component, inject, signal } from '@angular/core';
import { form, FormField, FormRoot, maxLength, requiredError, validate } from '@angular/forms/signals';
import { firstValueFrom } from 'rxjs';
import { QuotesApi } from '../services/quotes-api';
import { Quote } from '../models/quote.model';
import { AppError } from '../http/app-error.model';

interface CreateQuoteModel {
  author: string;
  text: string;
}

@Component({
  selector: 'app-create-quote-signal',
  imports: [FormField, FormRoot],
  templateUrl: './create-quote-signal.html',
  styleUrl: './create-quote-signal.css',
})
export class CreateQuoteSignal {
  private readonly quotesApi = inject(QuotesApi);

  readonly model = signal<CreateQuoteModel>({ author: '', text: '' });
  readonly createdQuote = signal<Quote | null>(null);
  readonly submitError = signal<string | null>(null);

  readonly quoteForm = form(
    this.model,
    (path) => {
      validate(path.author, (ctx) =>
        ctx.value().trim().length === 0 ? requiredError({ message: 'Author is required.' }) : undefined,
      );
      maxLength(path.author, 200, { message: 'Author must be 200 characters or fewer.' });

      validate(path.text, (ctx) =>
        ctx.value().trim().length === 0 ? requiredError({ message: 'Quote text is required.' }) : undefined,
      );
      maxLength(path.text, 1000, { message: 'Quote text must be 1000 characters or fewer.' });
    },
    {
      submission: {
        action: async (field) => {
          this.submitError.set(null);
          this.createdQuote.set(null);

          try {
            const quote = await firstValueFrom(this.quotesApi.createQuote(field().value()));
            this.createdQuote.set(quote);
            this.model.set({ author: '', text: '' });
            field().reset();
            this.quoteForm.author().focusBoundControl();
          } catch (err) {
            this.submitError.set((err as AppError).message);
          }

          return undefined;
        },
        onInvalid: () => {
          if (this.quoteForm.author().invalid()) {
            this.quoteForm.author().markAsTouched();
            this.quoteForm.author().focusBoundControl();
          } else if (this.quoteForm.text().invalid()) {
            this.quoteForm.text().markAsTouched();
            this.quoteForm.text().focusBoundControl();
          }
        },
      },
    },
  );
}
