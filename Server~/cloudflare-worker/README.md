# Content key server

A tiny server that hands out your content keys only to copies that prove they're entitled. Runs on Cloudflare Workers (free tier is plenty: one request per install per month), has no dependencies, and takes about ten minutes to deploy.

## Deploy

```bash
npm install -g wrangler
wrangler login
cd Server~/cloudflare-worker
wrangler deploy
```

Note the URL it prints (`https://gpk-key-server.<you>.workers.dev`) and put it in **ContentLockConfig → Key Server Url**.

## Secrets

```bash
# Required
wrangler secret put CONTENT_KEYS        # paste the JSON from Project Settings > Game Protection > "Copy keys for server"
wrangler secret put NONCE_SECRET        # any long random string, e.g. `openssl rand -hex 32`

# Steam (desktop builds)
wrangler secret put STEAM_WEB_API_KEY   # Steamworks PUBLISHER Web API key (Steamworks > Users & Permissions > Manage Groups)
wrangler secret put STEAM_APP_IDS       # "123456" or "123456,234567". PAID apps only. Never add a free demo.

# Google Play (Android builds, with the Play Integrity sample)
wrangler secret put GOOGLE_SERVICE_ACCOUNT_JSON   # service account JSON; enable the Play Integrity API for your app in Play Console
wrangler secret put ANDROID_PACKAGE_NAMES          # "com.yourstudio.yourgame"
wrangler secret put ANDROID_CERT_SHA256            # optional, your Play app-signing cert SHA-256

# Testing only. Remove before launch: `wrangler secret delete DEV_SHARED_SECRET`
wrangler secret put DEV_SHARED_SECRET
```

Whenever you rotate a key in Unity, update `CONTENT_KEYS` here **before** shipping the build.

## Why demos must stay off this list

A thief can edit a build's App ID to point at your free demo. If the demo's App ID were allowed here, anyone with the demo in their library would get the keys to the paid content. So:

- `STEAM_APP_IDS` lists paid apps only;
- the kit never embeds keys in demo builds;
- keep demo Addressables groups on the stock (unencrypted) provider. Demo content is free anyway.

## Hardening

- **Rate limit** `/v1/key` in the Cloudflare dashboard (Security → WAF → Rate limiting rules), e.g. 20 requests per minute per IP.
- **Watch the logs** (`wrangler tail`). Every grant and denial is logged with the build ID and channel, so a leaked build shows up as denials from an unexpected channel.
- **Shutting down someday?** Ship an update that switches ContentLockConfig to Local mode first, so players keep their content.

## Running it elsewhere

`worker.js` only uses `fetch`, `crypto.subtle` and `URL`, so it ports to Deno, Bun, Vercel Edge or any Node 18+ server with a thin wrapper. The protocol is documented at the top of the file.
