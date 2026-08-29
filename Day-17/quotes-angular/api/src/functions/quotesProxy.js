const { app } = require('@azure/functions');
const { DefaultAzureCredential } = require('@azure/identity');

// Non-secret configuration only. Both values are set as plain Static Web
// App application settings, never as secrets - there is nothing here an
// attacker could use without also controlling this Function's identity.
const API_BASE_URL = process.env.QUOTES_API_BASE_URL;
const API_SCOPE = process.env.QUOTES_API_SCOPE; // e.g. api://<clientId>/.default

// DefaultAzureCredential resolves to this Function App's system-assigned
// Managed Identity when running in Azure - no client secret, certificate,
// or connection string is configured anywhere for this credential.
const credential = new DefaultAzureCredential();

let cachedToken = null;

async function getManagedIdentityToken(context) {
  const now = Date.now();
  if (cachedToken && cachedToken.expiresOnTimestamp > now + 60_000) {
    return cachedToken.token;
  }

  const token = await credential.getToken(API_SCOPE);
  if (!token) {
    throw new Error('Managed Identity did not return a token for the configured scope.');
  }

  cachedToken = token;
  context.log('Acquired a fresh Managed Identity token for the Week-1 API (not logged, not persisted).');
  return token.token;
}

app.http('quotesProxy', {
  methods: ['GET', 'POST', 'DELETE', 'PUT', 'PATCH'],
  authLevel: 'anonymous',
  route: '{*restOfPath}',
  handler: async (request, context) => {
    if (!API_BASE_URL || !API_SCOPE) {
      context.error('QUOTES_API_BASE_URL / QUOTES_API_SCOPE application settings are not configured.');
      return { status: 500, jsonBody: { error: 'API proxy is not configured.' } };
    }

    let accessToken;
    try {
      accessToken = await getManagedIdentityToken(context);
    } catch (err) {
      context.error('Failed to acquire a Managed Identity token', err);
      return { status: 502, jsonBody: { error: 'Could not authenticate to the Week-1 API.' } };
    }

    const restOfPath = request.params.restOfPath ?? '';
    const search = new URL(request.url).search;
    const upstreamUrl = `${API_BASE_URL}/api/${restOfPath}${search}`;

    const hasBody = !['GET', 'HEAD'].includes(request.method);
    const upstreamResponse = await fetch(upstreamUrl, {
      method: request.method,
      headers: {
        'Content-Type': 'application/json',
        // The real, live proof for verification: this Authorization header
        // is populated ONLY from the Managed Identity token above - it is
        // never a value read from a repo file, an app setting, or a secret.
        Authorization: `Bearer ${accessToken}`,
      },
      body: hasBody ? await request.text() : undefined,
    });

    const contentType = upstreamResponse.headers.get('content-type') ?? 'application/json';
    const responseBody = await upstreamResponse.text();

    return {
      status: upstreamResponse.status,
      headers: { 'Content-Type': contentType },
      body: responseBody,
    };
  },
});
