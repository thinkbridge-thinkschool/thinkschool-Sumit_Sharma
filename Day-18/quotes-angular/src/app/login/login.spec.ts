import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { Login } from './login';

let store: Record<string, string>;

function setup(queryParams: Record<string, string> = {}) {
  store = {};
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => store[key] ?? null,
    setItem: (key: string, value: string) => {
      store[key] = value;
    },
    removeItem: (key: string) => {
      delete store[key];
    },
  });

  const navigateByUrl = vi.fn().mockResolvedValue(true);

  TestBed.configureTestingModule({
    imports: [Login],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: ActivatedRoute,
        useValue: { queryParamMap: of(convertToParamMap(queryParams)) },
      },
      { provide: Router, useValue: { navigateByUrl } },
    ],
  });

  const fixture = TestBed.createComponent(Login);
  const httpMock = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  return { fixture, httpMock, component: fixture.componentInstance, navigateByUrl };
}

describe('Login (soft sign-in form)', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('defaults the post-sign-in redirect to /quotes when no redirectTo param is present', () => {
    const { component } = setup();
    expect(component.redirectTo()).toBe('/quotes');
  });

  it('reads the redirectTo query param set by the auth guard', () => {
    const { component } = setup({ redirectTo: '/create' });
    expect(component.redirectTo()).toBe('/create');
  });

  it('does not sign in with a blank display name', () => {
    const { component, httpMock } = setup();
    component.submit();
    httpMock.expectNone('/dev-config/dev-token.local.json');
    expect(component.displayName.touched).toBe(true);
  });

  it('signs in and navigates to the redirect target on success', () => {
    const { component, httpMock, navigateByUrl } = setup({ redirectTo: '/create' });

    component.displayName.setValue('Ada');
    component.submit();

    httpMock.expectOne('/dev-config/dev-token.local.json').flush({ token: 'real-dev-token' });

    expect(navigateByUrl).toHaveBeenCalledWith('/create');
  });

  it('does not navigate if sign-in fails', () => {
    const { component, httpMock, navigateByUrl } = setup();

    component.displayName.setValue('Ada');
    component.submit();

    httpMock
      .expectOne('/dev-config/dev-token.local.json')
      .flush(null, { status: 404, statusText: 'Not Found' });

    expect(navigateByUrl).not.toHaveBeenCalled();
    expect(component.error()).toBeTruthy();
  });
});
