import { routes } from './app.routes';
import { authGuard } from './auth/auth.guard';

describe('app routes', () => {
  it('redirects the empty path to /quotes', () => {
    const root = routes.find((r) => r.path === '');
    expect(root?.redirectTo).toBe('quotes');
    expect(root?.pathMatch).toBe('full');
  });

  it('maps /quotes to the eagerly-loaded Quotes component (not lazy)', () => {
    const quotesRoute = routes.find((r) => r.path === 'quotes');
    expect(quotesRoute?.component).toBeDefined();
    expect(quotesRoute?.loadComponent).toBeUndefined();
  });

  it('maps /quotes/:id to a lazily-loaded detail component with the correct param name', () => {
    const detailRoute = routes.find((r) => r.path === 'quotes/:id');
    expect(detailRoute).toBeDefined();
    expect(typeof detailRoute?.loadComponent).toBe('function');
    // Regression guard for "wrong route parameter name": the route path
    // literally must use :id, since QuoteDetail reads paramMap.get('id').
    expect(detailRoute?.path).toContain(':id');
  });

  it('resolves the lazy detail chunk to the QuoteDetail component', async () => {
    const detailRoute = routes.find((r) => r.path === 'quotes/:id')!;
    const loaded = await (detailRoute.loadComponent as () => Promise<unknown>)();
    // Bundler output may mangle the class name (e.g. `_QuoteDetail`), so
    // match loosely rather than asserting an exact minified identifier.
    expect((loaded as { name: string }).name).toMatch(/QuoteDetail$/);
  });

  it('guards /create with authGuard and lazily loads it', () => {
    const createRoute = routes.find((r) => r.path === 'create');
    expect(createRoute?.canActivate).toEqual([authGuard]);
    expect(typeof createRoute?.loadComponent).toBe('function');
  });

  it('falls back unknown paths to /quotes', () => {
    const wildcard = routes.find((r) => r.path === '**');
    expect(wildcard?.redirectTo).toBe('quotes');
  });
});
