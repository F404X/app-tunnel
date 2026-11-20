import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { DatabaseSync } from "node:sqlite";
import worker from "../telemetry/worker/index.mjs";
import { validate } from "../telemetry/worker/schema.mjs";
import { hasAccess } from "../telemetry/shared/access.mjs";
import { onRequest } from "../telemetry/pages/functions/_middleware.js";

const fixtures = (await readFile(new URL("../docs/telemetry-examples.txt", import.meta.url), "utf8"))
    .trim().split(/\n\s*\n/).map(JSON.parse);
const schema = await readFile(new URL("../telemetry/worker/migrations/0001_rollups.sql", import.meta.url), "utf8");

function database() {
    const sqlite = new DatabaseSync(":memory:");
    sqlite.exec(schema);
    return {
        sqlite,
        prepare(sql) {
            return {
                run() { return sqlite.prepare(sql).run(); },
                bind(...values) { return { sql, values }; }
            };
        },
        async batch(queries) {
            sqlite.exec("BEGIN");
            try {
                const results = queries.map(({ sql, values }) => {
                    const statement = sqlite.prepare(sql);
                    return sql.startsWith("SELECT") ? { results: statement.all(...values) } : statement.run(...values);
                });
                sqlite.exec("COMMIT");
                return results;
            } catch (error) {
                sqlite.exec("ROLLBACK");
                throw error;
            }
        }
    };
}

function env(db = database()) {
    const rate = { limit: async () => ({ success: true }) };
    return { DB: db, INGEST_LIMITER: rate, ADMIN_LIMITER: rate };
}

function report(payload, headers = {}) {
    return new Request("https://worker.example/v1/reports", {
        method: "POST", headers: { "Content-Type": "application/json", ...headers }, body: JSON.stringify(payload)
    });
}

test("public schema is exact and rejects personal fields and unexpected content", async () => {
    for (const example of fixtures) assert.ok(validate(example));
    const handler = env();
    for (const value of [
        { ...fixtures[0], ip: "192.0.2.23" },
        { ...fixtures[1], error: "C:/Users/private" },
        { ...fixtures[2], events: ["username@example.invalid"] },
        { ...fixtures[2], version: "1.2.3\n" },
        { ...fixtures[0], protocols: [fixtures[0].protocols[0], fixtures[0].protocols[0]] },
        { ...fixtures[0], starts: -1 }
    ]) assert.equal((await worker.fetch(report(value), handler)).status, 400);
    assert.equal((await worker.fetch(report(fixtures[0], { "Content-Type": "text/plain" }), handler)).status, 415);
    assert.equal((await worker.fetch(report("x".repeat(5000)), handler)).status, 413);
    assert.equal(handler.DB.sqlite.prepare("SELECT COUNT(*) AS count FROM rollups").get().count, 0);
});

test("ingestion uses daily rollups and the budget fails closed atomically", async () => {
    const handler = env();
    for (const json of fixtures) assert.equal((await worker.fetch(report(json), handler)).status, 202);
    assert.equal((await worker.fetch(report(fixtures[0]), handler)).status, 202);
    const lookup = handler.DB.sqlite.prepare("SELECT total FROM rollups WHERE metric = 'starts'");
    assert.equal(lookup.get().total, 4);
    handler.DB.sqlite.exec("UPDATE daily_budget SET used = 4999");
    assert.equal((await worker.fetch(report(fixtures[0]), handler)).status, 429);
    assert.equal(lookup.get().total, 4);
    assert.equal(handler.DB.sqlite.prepare("SELECT used FROM daily_budget").get().used, 4999);
    assert.equal((await worker.fetch(report(fixtures[1]), { ...handler, DB: { prepare: () => { throw Error("no DB"); } } })).status, 503);
    assert.equal((await worker.fetch(report(fixtures[1]), { ...handler, INGEST_LIMITER: { limit: async () => ({ success: false }) } })).status, 429);
});

test("retention removes expired rollups and preserves current aggregates", async () => {
    const handler = env();
    handler.DB.sqlite.exec("INSERT INTO rollups VALUES ('2001-01-01','statistics','0.6.0','windows-11','','starts',2)");
    assert.equal((await worker.fetch(report(fixtures[0]), handler)).status, 202);
    await worker.scheduled({}, handler);
    assert.equal(handler.DB.sqlite.prepare("SELECT total FROM rollups WHERE metric = 'starts'").get().total, 2);
    assert.equal(handler.DB.sqlite.prepare("SELECT COUNT(*) AS count FROM rollups WHERE day = '2001-01-01'").get().count, 0);
});

test("Pages and the Worker require signed Access JWTs for every administrative route", async () => {
    const handler = env();
    for (const endpoint of ["/admin/summary", "/admin/data"]) {
        const result = await worker.fetch(new Request("https://worker.example" + endpoint), handler);
        assert.equal(result.status, 403);
    }
    for (const path of ["/", "/dashboard.js", "/api/summary", "/api/data"]) {
        assert.equal((await onRequest({
            request: new Request("https://pages.example" + path), env: handler, next: () => { throw Error("unauthenticated"); }
        })).status, 403);
    }

    const key = await crypto.subtle.generateKey({
        name: "RSASSA-PKCS1-v1_5", modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256"
    }, true, ["sign", "verify"]);
    const jwk = await crypto.subtle.exportKey("jwk", key.publicKey);
    jwk.kid = "test-key";
    const issuer = "https://app-tunnel-test.cloudflareaccess.com";
    const audience = "a".repeat(64);
    const clock = Math.floor(Date.now() / 1000);
    const fetchKeys = async (url, init) => {
        assert.equal(url, issuer + "/cdn-cgi/access/certs");
        assert.equal(init.redirect, "error");
        return Response.json({ keys: [jwk] });
    };
    const context = { ACCESS_ISSUER: issuer, ACCESS_AUD: audience };
    const encoding = data => Buffer.from(JSON.stringify(data)).toString("base64url");
    const signed = async (claims, alg = "RS256") => {
        const value = encoding({ alg, kid: jwk.kid }) + "." + encoding(claims);
        const signature = await crypto.subtle.sign("RSASSA-PKCS1-v1_5", key.privateKey, new TextEncoder().encode(value));
        return value + "." + Buffer.from(signature).toString("base64url");
    };
    const claims = { aud: [audience], iss: issuer, exp: clock + 300 };
    const make = value => new Request("https://pages.example/", { headers: { "Cf-Access-Jwt-Assertion": value } });
    const valid = await signed(claims);
    assert.ok(await hasAccess(make(valid), context, { fetchKeys, now: clock }));
    assert.ok(!await hasAccess(make(valid + "a"), context, { fetchKeys, now: clock }));
    assert.ok(!await hasAccess(make(await signed({ ...claims, exp: clock - 1 })), context, { fetchKeys, now: clock }));
    assert.ok(!await hasAccess(make(await signed(claims, "none")), context, { fetchKeys, now: clock }));
    assert.ok(!await hasAccess(make(valid), { ...context, ACCESS_AUD: "b".repeat(64) }, { fetchKeys, now: clock }));

    const protectedApp = { ...handler, ...context, DASHBOARD_ORIGIN: "https://pages.example" };
    assert.equal((await worker.fetch(report(fixtures[0]), protectedApp)).status, 202);
    const dashboard = await onRequest({ request: make(valid), env: protectedApp, next: () => new Response("dashboard") });
    assert.equal(dashboard.status, 200);
    assert.equal(dashboard.headers.get("Cache-Control"), "no-store");
    const proxy = { ...protectedApp, TELEMETRY: { fetch: (url, options) => worker.fetch(new Request(url, options), protectedApp) } };
    const authenticated = (path, options = {}) => new Request("https://pages.example" + path, {
        ...options, headers: { "Cf-Access-Jwt-Assertion": valid, ...options.headers }
    });
    const result = await onRequest({ request: authenticated("/api/summary?days=14"), env: proxy });
    assert.equal(result.status, 200);
    const metrics = await result.json();
    assert.equal(metrics.versions[0].starts, 2);
    assert.equal(metrics.protocols[0].attempts, 2);
    assert.equal((await onRequest({
        request: authenticated("/api/data", { method: "DELETE", headers: { Origin: "https://evil.example" } }),
        env: proxy
    })).status, 403);
    assert.equal((await onRequest({
        request: authenticated("/api/data", {
            method: "DELETE", headers: { Origin: "https://pages.example", "X-AppTunnel-Confirm": "purge" }
        }), env: proxy
    })).status, 204);
    assert.equal(protectedApp.DB.sqlite.prepare("SELECT COUNT(*) AS count FROM rollups").get().count, 0);
});
