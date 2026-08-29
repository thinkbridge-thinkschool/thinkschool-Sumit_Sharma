import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { AppError } from './app-error.model';

interface ProblemDetailsBody {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export function mapToAppError(error: unknown): AppError {
  if (!(error instanceof HttpErrorResponse)) {
    return { status: 0, message: 'An unexpected error occurred.' };
  }

  if (error.status === 0) {
    return {
      status: 0,
      message: 'Could not reach the Quotes API. Confirm it is running and try again.',
    };
  }

  const body = error.error as ProblemDetailsBody | null;

  if (body?.errors && typeof body.errors === 'object') {
    const messages = Object.values(body.errors).flat();
    return {
      status: error.status,
      message: messages.length > 0 ? messages.join(' ') : (body.title ?? 'The request was invalid.'),
      validationErrors: body.errors,
    };
  }

  if (error.status === 401 || error.status === 403) {
    return {
      status: error.status,
      message: 'You are not authorized to perform this action.',
    };
  }

  if (error.status === 404) {
    return {
      status: error.status,
      message: body?.detail ?? body?.title ?? 'The requested quote could not be found.',
    };
  }

  if (body?.title || body?.detail) {
    return {
      status: error.status,
      message: body.detail ?? body.title ?? 'The Quotes API returned an error.',
    };
  }

  return {
    status: error.status,
    message: 'The Quotes API request failed. Please try again.',
  };
}

export const errorMappingInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(catchError((error: unknown) => throwError(() => mapToAppError(error))));
