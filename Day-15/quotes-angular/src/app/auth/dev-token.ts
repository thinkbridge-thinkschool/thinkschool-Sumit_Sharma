export const DEV_TOKEN_STORAGE_KEY = 'QUOTES_API_DEV_TOKEN';

export function getDevBearerToken(): string | null {
  return localStorage.getItem(DEV_TOKEN_STORAGE_KEY);
}
