# Sigma JWT Embed Template for .NET

This sample shows the usual shape of a Sigma secure embed integration in C#:

1. Build the Sigma JWT payload on the server.
2. Sign it with your Sigma embed secret.
3. Return a short-lived embed URL from a backend endpoint.
4. Render that URL in an iframe on the frontend.

The embed secret must never be sent to the browser.

## Configure

Copy `.env.example` to `.env` and fill in your Sigma values:

```bash
cp .env.example .env
```

`SIGMA_EMBED_PATH` is the path portion from the Sigma embed URL you copied from Sigma.
For example, if Sigma gives you:

```text
https://app.sigmacomputing.com/embed/1-abc123/workbook/xyz
```

then use:

```text
SIGMA_BASE_URL=https://app.sigmacomputing.com
SIGMA_EMBED_PATH=1-abc123/workbook/xyz
```

`SIGMA_JWT_QUERY_PARAMETER` defaults to `:jwt`. Change it only if your Sigma embed configuration expects a different query parameter name.

## Run

```bash
dotnet run
```

Then open:

```text
http://localhost:5000
```

## Important

Sigma tenants can vary by embed configuration. If your Sigma admin page expects different claim names or optional claims, update `BuildSigmaPayload` in `Program.cs`. The sample keeps those claims in one place so they are easy to adjust.
