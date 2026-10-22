import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../src/Inv.Web/wwwroot/auth.js', import.meta.url), 'utf8');
let auth, stored, calls, destination;
const config = { authority: 'https://auth.example', clientId: 'demo', redirectUri: '', apiBaseUrl: 'https://app.example/api/' };
const reply = (access = 'access-new', refresh = 'refresh-new') => ({ ok: true, json: async () => ({ access_token: access, refresh_token: refresh, token_type: 'Bearer', expires_in: 900 }) });
beforeEach(async () => {
    stored = new Map(); calls = []; destination = undefined;
    globalThis.sessionStorage = { getItem: key => stored.get(key), setItem: (key, value) => stored.set(key, value), removeItem: key => stored.delete(key) };
    globalThis.document = { baseURI: 'https://app.example/' };
    globalThis.location = { href: 'https://app.example/auth/callback', pathname: '/auth/callback', assign: url => destination = url };
    globalThis.history = { replaceState() {} };
    auth = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}#${Math.random()}`);
});
const setTokens = expiresAt => stored.set('inv.auth.tokens', JSON.stringify({ access_token: 'old', refresh_token: 'refresh-old', expiresAt }));

test('login generates S256 PKCE and callback exchanges the original verifier', async () => {
    await auth.login(config);
    const pending = JSON.parse(stored.get('inv.auth.pending'));
    const url = new URL(destination);
    assert.equal(pending.verifier.length, 43);
    assert.equal(url.searchParams.get('code_challenge_method'), 'S256');
    assert.equal(url.searchParams.get('code_challenge'), Buffer.from(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(pending.verifier))).toString('base64url'));
    location.href += `?code=one-time&state=${pending.state}`;
    globalThis.fetch = async (url, options) => { calls.push(options); return reply(); };
    await auth.callback(config);
    assert.equal(calls[0].body.get('code_verifier'), pending.verifier);
    assert.equal(calls[0].body.get('redirect_uri'), 'https://app.example/auth/callback');
    assert.equal(stored.has('inv.auth.pending'), false);
});

test('invalid callback state is rejected without token exchange', async () => {
    await auth.login(config);
    location.href += '?code=stolen&state=wrong';
    globalThis.fetch = async () => { assert.fail('Must not exchange code'); };
    await assert.rejects(auth.callback(config), /valideres/);
});

test('concurrent expired requests share one refresh and retain rotated token', async () => {
    setTokens(0);
    globalThis.fetch = async (url, options) => {
        if (String(url).includes('/oauth/token')) { calls.push(options); await new Promise(resolve => setTimeout(resolve, 10)); return reply(); }
        assert.equal(options.headers.Authorization, 'Bearer access-new');
        return { status: 200, text: async () => '{}' };
    };
    await Promise.all([auth.request(config, config.apiBaseUrl + 'me'), auth.request(config, config.apiBaseUrl + 'me')]);
    assert.equal(calls.length, 1);
    assert.equal(JSON.parse(stored.get('inv.auth.tokens')).refresh_token, 'refresh-new');
});

test('401 refreshes once and retries once; 403 does not refresh', async () => {
    setTokens(Date.now() + 900000);
    let requests = 0;
    globalThis.fetch = async url => {
        if (String(url).includes('/oauth/token')) { calls.push(url); return reply(); }
        requests++; return { status: 401, text: async () => '' };
    };
    assert.equal((await auth.request(config, config.apiBaseUrl + 'me')).status, 401);
    assert.equal(calls.length, 1); assert.equal(requests, 2); assert.equal(auth.isSignedIn(), false);
    setTokens(Date.now() + 900000); calls = [];
    globalThis.fetch = async () => ({ status: 403, text: async () => '' });
    assert.equal((await auth.request(config, config.apiBaseUrl + 'manager-access')).status, 403);
    assert.equal(calls.length, 0);
});

test('rejects token leakage to other origins and API paths', async () => {
    setTokens(Date.now() + 900000);
    for (const url of ['https://evil.example/api/me', 'https://app.example/elsewhere']) await assert.rejects(auth.request(config, url), /API-adresse/);
});

test('logout clears local data and navigates to the specified signout endpoint', async () => {
    setTokens(0);
    auth.logout(config);
    assert.equal(stored.size, 0);
    assert.equal(new URL(destination).pathname, '/signout');
    assert.equal(new URL(destination).searchParams.get('returnUrl'), 'https://app.example/login');
});

test('expired login state is rejected', async () => {
    await auth.login(config);
    const pending = JSON.parse(stored.get('inv.auth.pending'));
    pending.created = Date.now() - 11 * 60 * 1000;
    stored.set('inv.auth.pending', JSON.stringify(pending));
    location.href += `?code=old&state=${pending.state}`;
    await assert.rejects(auth.callback(config), /valideres/);
});

test('failed refresh clears the session', async () => {
    setTokens(0);
    globalThis.fetch = async () => ({ ok: false });
    await assert.rejects(auth.request(config, config.apiBaseUrl + 'me'));
    assert.equal(auth.isSignedIn(), false);
});

test('refresh completing after logout cannot restore the session', async () => {
    setTokens(0);
    let complete;
    globalThis.fetch = () => new Promise(resolve => complete = resolve);
    const request = auth.request(config, config.apiBaseUrl + 'me');
    auth.logout(config);
    complete(reply());
    await assert.rejects(request, /afbrudt/);
    assert.equal(auth.isSignedIn(), false);
});
