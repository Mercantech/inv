const tokenKey = 'inv.auth.tokens';
const pendingKey = 'inv.auth.pending';
let refreshPromise;
let generation = 0;
const encode = bytes => btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
const random = () => encode(crypto.getRandomValues(new Uint8Array(32)));
const read = key => JSON.parse(sessionStorage.getItem(key) || 'null');

export async function login(config) {
    const verifier = random();
    const state = random();
    const redirectUri = config.redirectUri || new URL('auth/callback', document.baseURI).href;
    const challenge = encode(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier))));
    sessionStorage.setItem(pendingKey, JSON.stringify({ verifier, state, redirectUri, created: Date.now() }));
    const url = new URL('/oauth/authorize', config.authority);
    url.search = new URLSearchParams({ response_type: 'code', client_id: config.clientId, redirect_uri: redirectUri,
        state, code_challenge: challenge, code_challenge_method: 'S256' });
    location.assign(url.href);
}

async function exchange(config, body, expectedGeneration = generation) {
    const response = await fetch(new URL('/oauth/token', config.authority), {
        method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({ ...body, client_id: config.clientId }), credentials: 'omit'
    });
    if (!response.ok) throw new Error('Login kunne ikke fornyes eller gennemføres. Prøv at logge ind igen.');
    const tokens = await response.json();
    if (!tokens.access_token || !tokens.refresh_token || tokens.token_type?.toLowerCase() !== 'bearer'
        || !Number.isFinite(tokens.expires_in) || tokens.expires_in <= 0) throw new Error('Auth-serveren returnerede et ugyldigt token-svar.');
    if (expectedGeneration !== generation) throw new Error('Login blev afbrudt.');
    const stored = { access_token: tokens.access_token, refresh_token: tokens.refresh_token,
        expiresAt: Date.now() + tokens.expires_in * 1000 };
    sessionStorage.setItem(tokenKey, JSON.stringify(stored));
    return stored;
}

export async function callback(config) {
    const query = new URL(location.href).searchParams;
    const pending = read(pendingKey);
    sessionStorage.removeItem(pendingKey);
    history.replaceState(null, '', location.pathname);
    if (!pending || !query.get('state') || query.get('state') !== pending.state
        || Date.now() - pending.created > 10 * 60 * 1000) throw new Error('Login kunne ikke valideres. Start login igen.');
    if (query.has('error') || !query.get('code')) throw new Error('Login blev afvist eller afbrudt.');
    await exchange(config, { grant_type: 'authorization_code', code: query.get('code'),
        redirect_uri: pending.redirectUri, code_verifier: pending.verifier });
}

async function refresh(config, rejectedToken) {
    if (refreshPromise) return refreshPromise;
    const current = read(tokenKey);
    if (!current) throw new Error('Log ind for at fortsætte.');
    if (rejectedToken && current.access_token !== rejectedToken) return current;
    const expectedGeneration = generation;
    refreshPromise = exchange(config, { grant_type: 'refresh_token', refresh_token: current.refresh_token }, expectedGeneration)
        .catch(error => { if (expectedGeneration === generation) sessionStorage.removeItem(tokenKey); throw error; })
        .finally(() => { refreshPromise = undefined; });
    return refreshPromise;
}

export function isSignedIn() { return !!read(tokenKey); }

export async function request(config, url, method = 'GET', body = null) {
    // Send kun login-token til vores eget API.
    const target = new URL(url, document.baseURI);
    const api = new URL(config.apiBaseUrl, document.baseURI);

    if (target.origin !== api.origin
        || !target.pathname.startsWith(api.pathname)) {
        throw new Error('Ugyldig API-adresse.');
    }

    let tokens = read(tokenKey);

    if (!tokens) {
        throw new Error('Log ind for at fortsætte.');
    }

    if (tokens.expiresAt <= Date.now() + 30000) {
        tokens = await refresh(config);
    }

    const send = token => {
        const headers = {
            Authorization: `Bearer ${token}`
        };

        if (body !== null) {
            headers['Content-Type'] = 'application/json';
        }

        return fetch(target, {
            method,
            headers,
            body: body === null ? undefined : JSON.stringify(body),
            credentials: 'omit',
            redirect: 'error'
        });
    };

    let response = await send(tokens.access_token);

    // Forny login og prøv én gang mere ved et udløbet token.
    if (response.status === 401) {
        tokens = await refresh(config, tokens.access_token);
        response = await send(tokens.access_token);

        if (response.status === 401) {
            sessionStorage.removeItem(tokenKey);
        }
    }

    return {
        status: response.status,
        body: await response.text()
    };
}

export function logout(config) {
    generation++;
    sessionStorage.removeItem(tokenKey);
    sessionStorage.removeItem(pendingKey);
    const url = new URL('/signout', config.authority);
    url.searchParams.set('returnUrl', new URL('login', document.baseURI).href);
    location.assign(url.href);
}
