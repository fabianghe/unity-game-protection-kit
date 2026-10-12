/**
 * Game Protection Kit — content key server (Cloudflare Worker, no dependencies).
 *
 * Hands out content keys only to copies that prove entitlement:
 *   - Steam:         a Web API auth ticket, verified with ISteamUserAuth/AuthenticateUserTicket
 *   - Google Play:   a Play Integrity token, decoded with the Play Integrity API
 *   - dev (testing): a shared secret, only when DEV_SHARED_SECRET is configured
 *
 * Endpoints
 *   GET  /v1/nonce  -> { "nonce": "<base64url>" }               (10 minute validity, HMAC-signed, stateless)
 *   POST /v1/key    -> { "keys": [{ "id", "key" }], "ttlSeconds }  or  4xx { "error": "..." }
 *        body: { v:1, nonce, proof:{ type:"steam", appId, ticket } | { type:"play", packageName, token } | { type:"dev", secret },
 *                buildId, channel, platform, version }
 *
 * Secrets / vars (wrangler secret put NAME):
 *   CONTENT_KEYS            JSON array [{ "id": "hex16", "key": "base64" }]  (copy from Project Settings > Game Protection)
 *   NONCE_SECRET            any long random string
 *   STEAM_WEB_API_KEY       Steamworks *publisher* Web API key
 *   STEAM_APP_IDS           comma-separated PAID App IDs. Never list a free demo here.
 *   STEAM_CHECK_OWNERSHIP   "true" to also call CheckAppOwnership (default "true")
 *   STEAM_ALLOW_FAMILY_SHARING  "true" to accept Family Sharing (default "true")
 *   GOOGLE_SERVICE_ACCOUNT_JSON  service account JSON with the Play Integrity API enabled
 *   ANDROID_PACKAGE_NAMES   comma-separated allowed package names
 *   ANDROID_CERT_SHA256     optional comma-separated allowed signing cert SHA-256 (hex, no colons)
 *   PLAY_REQUIRE_DEVICE_INTEGRITY  default "true"
 *   PLAY_REQUIRE_LICENSED   default "true"  (accountDetails.appLicensingVerdict == LICENSED)
 *   DEV_SHARED_SECRET       testing only; leave unset in production
 *   KEY_TTL_SECONDS         default 2592000 (30 days)
 */

const NONCE_MAX_AGE_MS = 10 * 60 * 1000;
const PLAY_TOKEN_MAX_AGE_MS = 10 * 60 * 1000;

export default {
  async fetch(request, env) {
    try {
      const url = new URL(request.url);
      if (request.method === 'GET' && url.pathname === '/v1/nonce') return json(200, { nonce: await makeNonce(env) });
      if (request.method === 'POST' && url.pathname === '/v1/key') return await handleKey(request, env);
      if (request.method === 'GET' && url.pathname === '/') return json(200, { ok: true, service: 'gpk-key-server' });
      return json(404, { error: 'not_found' });
    } catch (e) {
      console.error('unhandled', e && e.message);
      return json(500, { error: 'internal' });
    }
  },
};

async function handleKey(request, env) {
  if (!env.CONTENT_KEYS) return json(500, { error: 'server_not_configured', message: 'CONTENT_KEYS missing' });

  let body;
  try { body = await request.json(); } catch { return json(400, { error: 'bad_json' }); }
  if (!body || body.v !== 1 || !body.proof || typeof body.proof.type !== 'string') return json(400, { error: 'bad_request' });

  const nonceCheck = await verifyNonce(env, body.nonce);
  if (!nonceCheck.ok) return json(400, { error: 'bad_nonce', message: nonceCheck.reason });

  let verdict;
  switch (body.proof.type) {
    case 'steam': verdict = await verifySteam(env, body.proof); break;
    case 'play': verdict = await verifyPlay(env, body.proof, body.nonce); break;
    case 'dev': verdict = verifyDev(env, body.proof); break;
    default: verdict = { ok: false, status: 400, reason: 'unknown_proof_type' };
  }

  if (!verdict.ok) {
    console.log(JSON.stringify({ denied: verdict.reason, type: body.proof.type, build: body.buildId, channel: body.channel, platform: body.platform }));
    return json(verdict.status || 403, { error: verdict.reason });
  }

  let keys;
  try {
    keys = JSON.parse(env.CONTENT_KEYS);
    if (!Array.isArray(keys) || keys.length === 0) throw new Error('empty');
  } catch {
    return json(500, { error: 'server_not_configured', message: 'CONTENT_KEYS must be a JSON array' });
  }

  console.log(JSON.stringify({ granted: body.proof.type, subject: verdict.subject, build: body.buildId, channel: body.channel, platform: body.platform }));
  return json(200, { keys, ttlSeconds: parseInt(env.KEY_TTL_SECONDS || '2592000', 10) });
}

// ---------- nonce (stateless: timestamp + random + HMAC) ----------

async function makeNonce(env) {
  const bytes = new Uint8Array(8 + 16);
  const now = BigInt(Date.now());
  for (let i = 0; i < 8; i++) bytes[i] = Number((now >> BigInt(8 * (7 - i))) & 0xffn);
  crypto.getRandomValues(bytes.subarray(8));
  const mac = (await hmac(env, bytes)).slice(0, 16);
  const out = new Uint8Array(bytes.length + mac.length);
  out.set(bytes); out.set(mac, bytes.length);
  return b64url(out);
}

async function verifyNonce(env, nonce) {
  if (typeof nonce !== 'string') return { ok: false, reason: 'missing' };
  let bytes;
  try { bytes = unb64url(nonce); } catch { return { ok: false, reason: 'malformed' }; }
  if (bytes.length !== 8 + 16 + 16) return { ok: false, reason: 'malformed' };
  const payload = bytes.subarray(0, 24);
  const mac = (await hmac(env, payload)).slice(0, 16);
  if (!constantTimeEqual(mac, bytes.subarray(24))) return { ok: false, reason: 'bad_signature' };
  let ts = 0n;
  for (let i = 0; i < 8; i++) ts = (ts << 8n) | BigInt(bytes[i]);
  const age = Date.now() - Number(ts);
  if (age < -60000 || age > NONCE_MAX_AGE_MS) return { ok: false, reason: 'expired' };
  return { ok: true };
}

async function hmac(env, data) {
  const secret = env.NONCE_SECRET;
  if (!secret) throw new Error('NONCE_SECRET missing');
  const key = await crypto.subtle.importKey('raw', new TextEncoder().encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  return new Uint8Array(await crypto.subtle.sign('HMAC', key, data));
}

// ---------- Steam ----------

async function verifySteam(env, proof) {
  if (!env.STEAM_WEB_API_KEY || !env.STEAM_APP_IDS) return { ok: false, status: 500, reason: 'steam_not_configured' };

  const appId = String(proof.appId || '');
  const allowed = env.STEAM_APP_IDS.split(',').map(s => s.trim()).filter(Boolean);
  if (!allowed.includes(appId)) return { ok: false, reason: 'app_not_allowed' };

  const ticket = String(proof.ticket || '');
  if (!/^[0-9A-Fa-f]{20,8192}$/.test(ticket)) return { ok: false, reason: 'bad_ticket' };

  const auth = new URL('https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/');
  auth.searchParams.set('key', env.STEAM_WEB_API_KEY);
  auth.searchParams.set('appid', appId);
  auth.searchParams.set('ticket', ticket);
  auth.searchParams.set('identity', 'gpk');

  const authResponse = await fetch(auth.toString());
  if (!authResponse.ok) return { ok: false, status: 502, reason: 'steam_unavailable:' + authResponse.status };
  const authJson = await authResponse.json();
  const params = authJson && authJson.response && authJson.response.params;
  if (!params || params.result !== 'OK') return { ok: false, reason: 'ticket_rejected' };
  if (params.vacbanned || params.publisherbanned) return { ok: false, reason: 'banned' };

  const steamId = params.steamid;
  const ownerId = params.ownersteamid;
  const familyShared = ownerId && ownerId !== steamId;
  if (familyShared && (env.STEAM_ALLOW_FAMILY_SHARING || 'true') !== 'true') return { ok: false, reason: 'family_sharing_not_allowed' };

  if ((env.STEAM_CHECK_OWNERSHIP || 'true') === 'true') {
    const own = new URL('https://partner.steam-api.com/ISteamUser/CheckAppOwnership/v3/');
    own.searchParams.set('key', env.STEAM_WEB_API_KEY);
    own.searchParams.set('steamid', familyShared ? ownerId : steamId);
    own.searchParams.set('appid', appId);
    const ownResponse = await fetch(own.toString());
    if (!ownResponse.ok) return { ok: false, status: 502, reason: 'steam_unavailable:' + ownResponse.status };
    const ownJson = await ownResponse.json();
    const ownership = ownJson && ownJson.appownership;
    if (!ownership || ownership.ownsapp !== true) return { ok: false, reason: 'not_owner' };
  }

  return { ok: true, subject: 'steam:' + steamId };
}

// ---------- Google Play Integrity ----------

let googleToken = { value: null, expiresAt: 0 };

async function verifyPlay(env, proof, nonce) {
  if (!env.GOOGLE_SERVICE_ACCOUNT_JSON || !env.ANDROID_PACKAGE_NAMES) return { ok: false, status: 500, reason: 'play_not_configured' };

  const packageName = String(proof.packageName || '');
  const allowed = env.ANDROID_PACKAGE_NAMES.split(',').map(s => s.trim()).filter(Boolean);
  if (!allowed.includes(packageName)) return { ok: false, reason: 'package_not_allowed' };
  if (typeof proof.token !== 'string' || proof.token.length < 20) return { ok: false, reason: 'bad_token' };

  const accessToken = await googleAccessToken(env);
  const response = await fetch(`https://playintegrity.googleapis.com/v1/${encodeURIComponent(packageName)}:decodeIntegrityToken`, {
    method: 'POST',
    headers: { Authorization: 'Bearer ' + accessToken, 'Content-Type': 'application/json' },
    body: JSON.stringify({ integrity_token: proof.token }),
  });
  if (!response.ok) return { ok: false, reason: response.status === 400 ? 'token_rejected' : 'play_unavailable:' + response.status, status: response.status === 400 ? 403 : 502 };

  const payload = (await response.json()).tokenPayloadExternal || {};
  const details = payload.requestDetails || {};
  const app = payload.appIntegrity || {};
  const device = payload.deviceIntegrity || {};
  const account = payload.accountDetails || {};

  if (details.requestPackageName !== packageName) return { ok: false, reason: 'package_mismatch' };
  if (details.nonce !== nonce) return { ok: false, reason: 'nonce_mismatch' };
  const age = Date.now() - Number(details.timestampMillis || 0);
  if (!(age >= -60000 && age <= PLAY_TOKEN_MAX_AGE_MS)) return { ok: false, reason: 'token_expired' };

  if (app.appRecognitionVerdict !== 'PLAY_RECOGNIZED') return { ok: false, reason: 'app_not_recognized' };
  if (app.packageName && app.packageName !== packageName) return { ok: false, reason: 'package_mismatch' };
  if (env.ANDROID_CERT_SHA256) {
    const want = env.ANDROID_CERT_SHA256.split(',').map(s => s.trim().replace(/:/g, '').toLowerCase()).filter(Boolean);
    const have = (app.certificateSha256Digest || []).map(d => Array.from(unb64url(d)).map(b => b.toString(16).padStart(2, '0')).join(''));
    if (!have.some(h => want.includes(h))) return { ok: false, reason: 'cert_mismatch' };
  }

  if ((env.PLAY_REQUIRE_DEVICE_INTEGRITY || 'true') === 'true') {
    const verdicts = device.deviceRecognitionVerdict || [];
    if (!verdicts.includes('MEETS_DEVICE_INTEGRITY') && !verdicts.includes('MEETS_STRONG_INTEGRITY')) return { ok: false, reason: 'device_integrity' };
  }
  if ((env.PLAY_REQUIRE_LICENSED || 'true') === 'true' && account.appLicensingVerdict !== 'LICENSED') return { ok: false, reason: 'not_licensed' };

  return { ok: true, subject: 'play:' + packageName };
}

async function googleAccessToken(env) {
  if (googleToken.value && Date.now() < googleToken.expiresAt - 60000) return googleToken.value;

  const account = JSON.parse(env.GOOGLE_SERVICE_ACCOUNT_JSON);
  const now = Math.floor(Date.now() / 1000);
  const header = b64url(new TextEncoder().encode(JSON.stringify({ alg: 'RS256', typ: 'JWT' })));
  const claims = b64url(new TextEncoder().encode(JSON.stringify({
    iss: account.client_email,
    scope: 'https://www.googleapis.com/auth/playintegrity',
    aud: 'https://oauth2.googleapis.com/token',
    iat: now,
    exp: now + 3600,
  })));
  const signingInput = header + '.' + claims;
  const key = await crypto.subtle.importKey('pkcs8', pemToDer(account.private_key), { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['sign']);
  const signature = new Uint8Array(await crypto.subtle.sign('RSASSA-PKCS1-v1_5', key, new TextEncoder().encode(signingInput)));
  const assertion = signingInput + '.' + b64url(signature);

  const response = await fetch('https://oauth2.googleapis.com/token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer', assertion }),
  });
  if (!response.ok) throw new Error('google token exchange failed: ' + response.status);
  const data = await response.json();
  googleToken = { value: data.access_token, expiresAt: Date.now() + (data.expires_in || 3600) * 1000 };
  return googleToken.value;
}

// ---------- dev (testing only) ----------

function verifyDev(env, proof) {
  if (!env.DEV_SHARED_SECRET) return { ok: false, reason: 'dev_disabled' };
  const given = new TextEncoder().encode(String(proof.secret || ''));
  const want = new TextEncoder().encode(env.DEV_SHARED_SECRET);
  if (given.length !== want.length || !constantTimeEqual(given, want)) return { ok: false, reason: 'bad_secret' };
  return { ok: true, subject: 'dev' };
}

// ---------- helpers ----------

function json(status, data) {
  return new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' } });
}

function b64url(bytes) {
  let s = '';
  for (const b of bytes) s += String.fromCharCode(b);
  return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function unb64url(text) {
  const padded = text.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((text.length + 3) % 4);
  const s = atob(padded);
  const out = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) out[i] = s.charCodeAt(i);
  return out;
}

function pemToDer(pem) {
  const body = pem.replace(/-----BEGIN [A-Z ]+-----/, '').replace(/-----END [A-Z ]+-----/, '').replace(/\s+/g, '');
  const s = atob(body);
  const out = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) out[i] = s.charCodeAt(i);
  return out.buffer;
}

function constantTimeEqual(a, b) {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i];
  return diff === 0;
}
