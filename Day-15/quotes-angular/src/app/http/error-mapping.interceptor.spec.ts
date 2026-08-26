import { HttpErrorResponse } from '@angular/common/http';
import { mapToAppError } from './error-mapping.interceptor';

describe('mapToAppError', () => {
  it('maps a real ValidationProblemDetails body into a friendly message with validationErrors', () => {
    // Captured from a real POST /api/quotes response with an empty author/text.
    const error = new HttpErrorResponse({
      status: 400,
      error: {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { quote: ['Author must be between 1 and 200 characters.'] },
        traceId: '00-abc-def-01',
      },
    });

    const appError = mapToAppError(error);

    expect(appError.status).toBe(400);
    expect(appError.message).toBe('Author must be between 1 and 200 characters.');
    expect(appError.validationErrors).toEqual({
      quote: ['Author must be between 1 and 200 characters.'],
    });
  });

  it('maps a bare 404 with no body into a friendly not-found message', () => {
    // Ground truth: GET /api/quotes/{id} returns a body-less 404.
    const error = new HttpErrorResponse({ status: 404, error: null });

    const appError = mapToAppError(error);

    expect(appError.status).toBe(404);
    expect(appError.message).toBe('The requested quote could not be found.');
  });

  it('maps a bare 401 with no body into a friendly authorization message', () => {
    // Ground truth: POST /api/quotes without a token returns a body-less 401.
    const error = new HttpErrorResponse({ status: 401, error: null });

    const appError = mapToAppError(error);

    expect(appError.status).toBe(401);
    expect(appError.message).toBe('You are not authorized to perform this action.');
  });

  it('maps a network failure (status 0) into a reachability message', () => {
    const error = new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') });

    const appError = mapToAppError(error);

    expect(appError.status).toBe(0);
    expect(appError.message).toContain('Could not reach the Quotes API');
  });

  it('passes through a non-HttpErrorResponse as a generic error', () => {
    const appError = mapToAppError(new Error('boom'));

    expect(appError.status).toBe(0);
    expect(appError.message).toBe('An unexpected error occurred.');
  });
});
