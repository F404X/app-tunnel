import { hasAccess } from "../../shared/access.mjs";

const headers = {
    "Cache-Control": "no-store",
    "Content-Security-Policy": "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; font-src 'self'; base-uri 'none'; frame-ancestors 'none'",
    "Referrer-Policy": "no-referrer",
    "X-Content-Type-Options": "nosniff"
};

export async function onRequest({ request, env, next }) {
    if (!(await hasAccess(request, env))) return new Response("Access required", { status: 403, headers });
    const url = new URL(request.url);
    let result;
    if (url.pathname.startsWith("/api/")) {
        const mapping = { "/api/summary": "/admin/summary", "/api/data": "/admin/data" };
        const path = mapping[url.pathname];
        if (!path) return new Response(null, { status: 404, headers });
        if (request.method === "DELETE" && request.headers.get("Origin") !== url.origin)
            return new Response(null, { status: 403, headers });
        if (!env.TELEMETRY) return new Response(null, { status: 503, headers });
        const forwarded = new Headers();
        for (const name of ["Cf-Access-Jwt-Assertion", "Origin", "X-AppTunnel-Confirm"]) {
            const value = request.headers.get(name);
            if (value) forwarded.set(name, value);
        }
        result = await env.TELEMETRY.fetch("https://internal" + path + url.search, { method: request.method, headers: forwarded });
    } else {
        result = await next();
    }
    const protectedResponse = new Response(result.body, result);
    for (const [name, value] of Object.entries(headers)) protectedResponse.headers.set(name, value);
    return protectedResponse;
}
