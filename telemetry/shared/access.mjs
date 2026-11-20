const cache = new Map();
const utf8 = new TextEncoder();

function decode(data) {
    if (!/^[A-Za-z0-9_-]+$/.test(data)) throw new Error("invalid JWT encoding");
    return Uint8Array.from(atob(data.replaceAll("-", "+").replaceAll("_", "/")), char => char.charCodeAt(0));
}

function decodeJson(segment) {
    return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(decode(segment)));
}

async function signingKeys(issuer, fetchKeys, now) {
    const saved = cache.get(issuer);
    if (saved?.until > now) return saved.keys;
    const response = await fetchKeys(issuer + "/cdn-cgi/access/certs", { redirect: "error" });
    if (!response.ok) throw new Error("unavailable JWKS");
    const json = await response.json();
    if (!Array.isArray(json.keys) || json.keys.length > 16) throw new Error("invalid JWKS");
    const keys = await Promise.all(json.keys.filter(key => key.kty === "RSA" && key.kid && key.n && key.e)
        .map(async key => [key.kid, await crypto.subtle.importKey("jwk", key, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["verify"])]));
    const byId = new Map(keys);
    cache.set(issuer, { keys: byId, until: now + 300 });
    return byId;
}

export async function hasAccess(request, config, { fetchKeys = fetch, now = Math.floor(Date.now() / 1000) } = {}) {
    const { ACCESS_ISSUER: issuer, ACCESS_AUD: audience } = config;
    if (!/^https:\/\/[a-z0-9][a-z0-9-]*\.cloudflareaccess\.com$/.test(issuer ?? "") ||
        !/^[a-f0-9]{64}$/.test(audience ?? "")) return false;
    const token = request.headers.get("Cf-Access-Jwt-Assertion");
    if (!token || token.length > 8000) return false;
    try {
        const segments = token.split(".");
        if (segments.length !== 3) return false;
        const [header, payload, signature] = segments;
        const { alg, kid } = decodeJson(header);
        const claims = decodeJson(payload);
        if (alg !== "RS256" || typeof kid !== "string" || !Array.isArray(claims.aud) ||
            !claims.aud.includes(audience) || claims.iss !== issuer ||
            !Number.isSafeInteger(claims.exp) || claims.exp <= now ||
            (claims.nbf != null && (!Number.isSafeInteger(claims.nbf) || claims.nbf > now))) return false;
        const key = (await signingKeys(issuer, fetchKeys, now)).get(kid);
        return Boolean(key && await crypto.subtle.verify("RSASSA-PKCS1-v1_5", key, decode(signature), utf8.encode(header + "." + payload)));
    } catch {
        return false;
    }
}
