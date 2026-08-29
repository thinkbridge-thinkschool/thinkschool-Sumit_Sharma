const path = require('node:path');
const express = require('express');
const compression = require('compression');
const { DefaultAzureCredential } = require('@azure/identity');

// Non-secret configuration only - both come from plain Container App
// environment variables, never secrets. Neither value is usable by an
// attacker without also controlling this container's own identity.
const API_BASE_URL = process.env.QUOTES_API_BASE_URL;
const API_SCOPE = process.env.QUOTES_API_SCOPE; // e.g. api://<clientId>/.default
const PORT = process.env.PORT || 8080;
const PUBLIC_DIR = path.join(__dirname, 'public');

// Resolves to this container's own system-assigned Managed Identity when
// running in Azure Container Apps - no client secret, certificate, or
// connection string is configured anywhere for this credential.
const credential = new DefaultAzureCredential();
let cachedToken = null;

async function getManagedIdentityToken() {
  const now = Date.now();
  if (cachedToken && cachedToken.expiresOnTimestamp > now + 60_000) {
    return cachedToken.token;
  }
  const token = await credential.getToken(API_SCOPE);
  if (!token) {
    throw new Error('Managed Identity did not return a token for the configured scope.');
  }
  cachedToken = token;
  console.log('Acquired a fresh Managed Identity token for the Week-1 API (not logged, not persisted).');
  return token.token;
}

const app = express();

app.use(compression());

// Same security headers a Static Web Apps deployment would set via
// staticwebapp.config.json - this server plays that role instead.
app.use((req, res, next) => {
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('X-Frame-Options', 'DENY');
  res.setHeader('Referrer-Policy', 'strict-origin-when-cross-origin');
  res.setHeader(
    'Content-Security-Policy',
    "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; base-uri 'self'; frame-ancestors 'none'",
  );
  next();
});

app.use(express.text({ type: '*/*', limit: '2mb' }));

app.all('/api/*', async (req, res) => {
  if (!API_BASE_URL || !API_SCOPE) {
    console.error('QUOTES_API_BASE_URL / QUOTES_API_SCOPE are not configured.');
    return res.status(500).json({ error: 'API proxy is not configured.' });
  }

  let accessToken;
  try {
    accessToken = await getManagedIdentityToken();
  } catch (err) {
    console.error('Failed to acquire a Managed Identity token', err);
    return res.status(502).json({ error: 'Could not authenticate to the Week-1 API.' });
  }

  const restOfPath = req.params[0];
  const search = req.url.includes('?') ? `?${req.url.split('?')[1]}` : '';
  const upstreamUrl = `${API_BASE_URL}/api/${restOfPath}${search}`;

  const hasBody = !['GET', 'HEAD'].includes(req.method);
  const upstreamResponse = await fetch(upstreamUrl, {
    method: req.method,
    headers: {
      'Content-Type': 'application/json',
      // The real, live proof: this Authorization header is populated ONLY
      // from the Managed Identity token above - never a repo/app-setting value.
      Authorization: `Bearer ${accessToken}`,
    },
    body: hasBody && req.body ? req.body : undefined,
  });

  const contentType = upstreamResponse.headers.get('content-type') ?? 'application/json';
  const body = await upstreamResponse.text();
  res.status(upstreamResponse.status).setHeader('Content-Type', contentType).send(body);
});

app.use(
  express.static(PUBLIC_DIR, {
    // Angular's build output fingerprints these filenames with a content
    // hash, so a long-lived cache is safe: a changed file gets a new name.
    setHeaders: (res, filePath) => {
      if (!filePath.endsWith('index.html')) {
        res.setHeader('Cache-Control', 'public, max-age=31536000, immutable');
      }
    },
  }),
);

// SPA fallback for real Angular routes (e.g. /quotes/5) on a hard reload.
app.get('*', (req, res) => {
  res.sendFile(path.join(PUBLIC_DIR, 'index.html'));
});

app.listen(PORT, () => {
  console.log(`quotes-web listening on :${PORT}`);
});
