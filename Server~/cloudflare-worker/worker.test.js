// Tests for the key server. No dependencies: `npm test` (Node 18+).
// Steam and Google are replaced by a fake `fetch`, so nothing here touches the network.

import test from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPairSync } from 'node:crypto';
import worker from './worker.js';

const KEYS = [{ id: '0123456789abcdef', key: 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=' }];
const PAID_APP = '1000';
const DEMO_APP = '2000';
const PACKAGE = 'com.studio.game';

const baseEnv = {
  CONTENT_KEYS: JSON.stringify(KEYS),
  NONCE_SECRET: 'test-nonce-secret-test-nonce-secret',
  STEAM_WEB_API_KEY: 'steam-key',
  STEAM_APP_IDS: PAID_APP,
  ANDROID_PACKAGE_NAMES: PACKAGE,
  GOOGLE_SERVICE_ACCOUNT_JSON: JSON.stringify({
    client_email: 'svc@example.iam.gserviceaccount.com',
    private_key: generateKeyPairSync('rsa', { modulusLength: 2048 }).privateKey.export({ type: 'pkcs8', format: 'pem' }),
  }),
};

const call = (env, path, init) => worker.fetch(new Request('https://keys.test' + path, init), env);

async function nonce(env = baseEnv) {
  const response = await call(env, '/v1/nonce');
  assert.equal(response.status, 200);
  return (await response.json()).nonce;
}

async function requestKey(env, proof, overrides = {}) {
  const body = { v: 1, nonce: await nonce(env), proof, buildId: 'abc123', channel: 'full', platform: 'test', ...overrides };
  const response = await call(env, '/v1/key', { method: 'POST', body: JSON.stringify(body) });
  return { status: response.status, body: await response.json() };
}

/** Replaces global fetch for one test. `routes` maps a URL substring to a JSON body or a function (url, init) => body. */
function fakeFetch(t, routes) {
  const calls = [];
  t.mock.method(globalThis, 'fetch', async (url, init) => {
    url = String(url);
    calls.push(url);
    for (const [match, reply] of Object.entries(routes)) {
      if (!url.includes(match)) continue;
      const value = typeof reply === 'function' ? reply(url, init) : reply;
      return new Response(JSON.stringify(value.body ?? value), { status: value.status ?? 200 });
    }
    throw new Error('unexpected fetch: ' + url);
  });
  return calls;
}

const steamOk = (owner = '76561190000000001') => ({
  AuthenticateUserTicket: { response: { params: { result: 'OK', steamid: '76561190000000001', ownersteamid: owner } } },
  CheckAppOwnership: { appownership: { ownsapp: true } },
});

// ---------- nonce ----------

test('nonce is 56 url-safe characters with no padding', async () => {
  const value = await nonce();
  assert.match(value, /^[A-Za-z0-9_-]{56}$/);
  assert.notEqual(value, await nonce());
});

test('a nonce that was changed, truncated or made up is rejected', async () => {
  const good = await nonce();
  const flipped = (good[0] === 'A' ? 'B' : 'A') + good.slice(1);
  for (const bad of [flipped, good.slice(0, -4), 'A'.repeat(56), '', undefined, 42]) {
    const { status, body } = await requestKey({ ...baseEnv, DEV_SHARED_SECRET: 's3cret' }, { type: 'dev', secret: 's3cret' }, { nonce: bad });
    assert.equal(status, 400, String(bad));
    assert.equal(body.error, 'bad_nonce');
  }
});

test('a nonce expires after ten minutes', async (t) => {
  const env = { ...baseEnv, DEV_SHARED_SECRET: 's3cret' };
  const old = await nonce(env);
  const realNow = Date.now;
  t.mock.method(Date, 'now', () => realNow() + 11 * 60 * 1000);
  const { status, body } = await requestKey(env, { type: 'dev', secret: 's3cret' }, { nonce: old });
  assert.equal(status, 400);
  assert.equal(body.message, 'expired');
});

test('a nonce signed with another secret is rejected', async () => {
  const foreign = await nonce({ ...baseEnv, NONCE_SECRET: 'someone-elses-secret' });
  const { status } = await requestKey({ ...baseEnv, DEV_SHARED_SECRET: 's3cret' }, { type: 'dev', secret: 's3cret' }, { nonce: foreign });
  assert.equal(status, 400);
});

// ---------- request handling ----------

test('missing configuration fails closed', async () => {
  assert.equal((await call({ ...baseEnv, NONCE_SECRET: '' }, '/v1/nonce')).status, 500);
  const noKeys = await call({ ...baseEnv, CONTENT_KEYS: '' }, '/v1/key', { method: 'POST', body: '{}' });
  assert.equal(noKeys.status, 500);
});

test('malformed and oversized requests are rejected before any proof is checked', async (t) => {
  const calls = fakeFetch(t, {});
  const post = body => call(baseEnv, '/v1/key', { method: 'POST', body });
  assert.equal((await post('not json')).status, 400);
  assert.equal((await post('{"v":2}')).status, 400);
  assert.equal((await post(JSON.stringify({ v: 1, proof: { type: 'steam' }, pad: 'x'.repeat(70000) }))).status, 413);
  assert.equal((await call(baseEnv, '/v1/elsewhere')).status, 404);
  assert.equal(calls.length, 0);
});

test('unknown proof types are refused', async () => {
  const { status, body } = await requestKey(baseEnv, { type: 'epic', token: 'x' });
  assert.equal(status, 400);
  assert.equal(body.error, 'unknown_proof_type');
});

// ---------- dev secret ----------

test('dev secret works only when configured, and only when it matches', async () => {
  const env = { ...baseEnv, DEV_SHARED_SECRET: 's3cret' };
  const granted = await requestKey(env, { type: 'dev', secret: 's3cret' });
  assert.equal(granted.status, 200);
  assert.deepEqual(granted.body.keys, KEYS);
  assert.equal(granted.body.ttlSeconds, 2592000);

  assert.equal((await requestKey(env, { type: 'dev', secret: 'wrong!' })).status, 403);
  assert.equal((await requestKey(env, { type: 'dev' })).status, 403);
  assert.equal((await requestKey(baseEnv, { type: 'dev', secret: 's3cret' })).body.error, 'dev_disabled');
});

test('KEY_TTL_SECONDS is passed on, and a broken value falls back to 30 days', async () => {
  const env = { ...baseEnv, DEV_SHARED_SECRET: 's3cret' };
  assert.equal((await requestKey({ ...env, KEY_TTL_SECONDS: '3600' }, { type: 'dev', secret: 's3cret' })).body.ttlSeconds, 3600);
  assert.equal((await requestKey({ ...env, KEY_TTL_SECONDS: 'soon' }, { type: 'dev', secret: 's3cret' })).body.ttlSeconds, 2592000);
});

// ---------- Steam ----------

test('steam: an owner of the paid app gets the keys', async (t) => {
  const calls = fakeFetch(t, steamOk());
  const { status, body } = await requestKey(baseEnv, { type: 'steam', appId: Number(PAID_APP), ticket: 'AB'.repeat(40) });
  assert.equal(status, 200);
  assert.deepEqual(body.keys, KEYS);
  assert.equal(calls.length, 2);
  assert.ok(calls[0].includes('appid=' + PAID_APP) && calls[0].includes('identity=gpk'));
});

test('steam: the demo app id never reaches Steam and never gets keys', async (t) => {
  const calls = fakeFetch(t, steamOk());
  const { status, body } = await requestKey(baseEnv, { type: 'steam', appId: Number(DEMO_APP), ticket: 'AB'.repeat(40) });
  assert.equal(status, 403);
  assert.equal(body.error, 'app_not_allowed');
  assert.equal(calls.length, 0);
});

test('steam: rejected tickets, non-owners and garbage tickets are refused', async (t) => {
  const proof = { type: 'steam', appId: PAID_APP, ticket: 'AB'.repeat(40) };

  fakeFetch(t, { AuthenticateUserTicket: { response: { error: { errorcode: 101, errordesc: 'Invalid ticket' } } } });
  assert.equal((await requestKey(baseEnv, proof)).body.error, 'ticket_rejected');

  t.mock.restoreAll();
  fakeFetch(t, { ...steamOk(), CheckAppOwnership: { appownership: { ownsapp: false } } });
  assert.equal((await requestKey(baseEnv, proof)).body.error, 'not_owner');

  t.mock.restoreAll();
  const calls = fakeFetch(t, steamOk());
  assert.equal((await requestKey(baseEnv, { ...proof, ticket: 'not-hex!' })).body.error, 'bad_ticket');
  assert.equal(calls.length, 0);
});

test('steam: family sharing checks the lender, and can be switched off', async (t) => {
  const lender = '76561190000000099';
  const calls = fakeFetch(t, steamOk(lender));
  const proof = { type: 'steam', appId: PAID_APP, ticket: 'AB'.repeat(40) };

  assert.equal((await requestKey(baseEnv, proof)).status, 200);
  assert.ok(calls[1].includes('steamid=' + lender));

  const strict = await requestKey({ ...baseEnv, STEAM_ALLOW_FAMILY_SHARING: 'false' }, proof);
  assert.equal(strict.body.error, 'family_sharing_not_allowed');
});

test('steam: an outage is a 502, not a grant', async (t) => {
  fakeFetch(t, { AuthenticateUserTicket: { status: 503, body: {} } });
  const { status } = await requestKey(baseEnv, { type: 'steam', appId: PAID_APP, ticket: 'AB'.repeat(40) });
  assert.equal(status, 502);
});

// ---------- Google Play ----------

/** A Play verdict for the nonce in the request. `change` edits it to simulate one thing being wrong. */
async function requestPlayKey(t, change = () => {}) {
  const value = await nonce();
  const verdict = {
    requestDetails: { requestPackageName: PACKAGE, nonce: value, timestampMillis: String(Date.now()) },
    appIntegrity: { appRecognitionVerdict: 'PLAY_RECOGNIZED', packageName: PACKAGE, certificateSha256Digest: [] },
    deviceIntegrity: { deviceRecognitionVerdict: ['MEETS_DEVICE_INTEGRITY'] },
    accountDetails: { appLicensingVerdict: 'LICENSED' },
  };
  change(verdict);
  fakeFetch(t, {
    'oauth2.googleapis.com/token': { access_token: 'ya29.test', expires_in: 3600 },
    decodeIntegrityToken: { tokenPayloadExternal: verdict },
  });
  const result = await requestKey(baseEnv, { type: 'play', packageName: PACKAGE, token: 'x'.repeat(200) }, { nonce: value });
  t.mock.restoreAll();
  return result;
}

test('play: a licensed, recognized install gets the keys', async (t) => {
  const { status, body } = await requestPlayKey(t);
  assert.equal(status, 200);
  assert.deepEqual(body.keys, KEYS);
});

test('play: each failed condition is refused', async (t) => {
  const cases = {
    nonce_mismatch: v => { v.requestDetails.nonce = 'A'.repeat(56); },
    package_mismatch: v => { v.requestDetails.requestPackageName = 'com.thief.clone'; },
    token_expired: v => { v.requestDetails.timestampMillis = String(Date.now() - 60 * 60 * 1000); },
    app_not_recognized: v => { v.appIntegrity.appRecognitionVerdict = 'UNRECOGNIZED_VERSION'; },
    device_integrity: v => { v.deviceIntegrity.deviceRecognitionVerdict = []; },
    not_licensed: v => { v.accountDetails.appLicensingVerdict = 'UNLICENSED'; },
  };
  for (const [reason, change] of Object.entries(cases)) {
    const { status, body } = await requestPlayKey(t, change);
    assert.equal(status, 403, reason);
    assert.equal(body.error, reason);
  }
});

test('play: an unknown package name is refused without calling Google', async (t) => {
  const calls = fakeFetch(t, {});
  const { body } = await requestKey(baseEnv, { type: 'play', packageName: 'com.thief.clone', token: 'x'.repeat(200) });
  assert.equal(body.error, 'package_not_allowed');
  assert.equal(calls.length, 0);
});
