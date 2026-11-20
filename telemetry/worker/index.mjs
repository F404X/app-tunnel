import { hasAccess } from "../shared/access.mjs";
import { dimensions, protocols, readReport, systems, validVersion } from "./schema.mjs";

const UPSERT = `INSERT INTO rollups(day,kind,version,os,protocol,metric,total)
VALUES (?,?,?,?,?,?,?) ON CONFLICT(day,kind,version,os,protocol,metric)
DO UPDATE SET total = total + excluded.total`;

function date(time = Date.now()) {
    return new Date(time).toISOString().slice(0, 10);
}

function response(status, body) {
    const headers = { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" };
    if (status === 429 || status === 503) headers["Retry-After"] = "60";
    return body ? Response.json(body, { status, headers }) : new Response(null, { status, headers });
}

async function ingest(request, env) {
    if (!(await env.INGEST_LIMITER.limit({ key: "public-ingest" })).success) return response(429);
    const { report, status } = await readReport(request);
    if (!report) return response(status);
    const today = date();
    const statements = dimensions(report).map(([protocol, metric, total]) =>
        env.DB.prepare(UPSERT).bind(today, report.kind, report.version, report.os, protocol, metric, total));
    if (statements.length === 0) return response(400);
    try {
        await env.DB.batch(statements);
        return response(202);
    } catch (error) {
        return response(String(error?.message).includes("apptunnel-daily-budget") ? 429 : 503);
    }
}

async function summary(url, env) {
    const days = Number(url.searchParams.get("days") ?? "14");
    const protocol = url.searchParams.get("protocol") ?? "";
    const os = url.searchParams.get("os") ?? "";
    const version = url.searchParams.get("version") ?? "";
    if (![7, 14, 30].includes(days) || (protocol && !protocols.includes(protocol)) ||
        (os && !systems.includes(os)) || (version && !validVersion(version))) return response(400);
    const start = date(Date.now() - (days - 1) * 86400000);
    const where = "day >= ? AND (? = '' OR version = ?) AND (? = '' OR os = ?) AND (? = '' OR protocol = ? OR protocol = '')";
    const filters = [start, version, version, os, os, protocol, protocol];
    const statements = [
        env.DB.prepare(`SELECT day,kind,metric,SUM(total) AS total FROM rollups WHERE ${where}
            GROUP BY day,kind,metric ORDER BY day`).bind(...filters),
        env.DB.prepare(`SELECT version,SUM(total) AS starts FROM rollups WHERE day >= ? AND kind = 'statistics'
            AND metric = 'starts' AND (? = '' OR os = ?) GROUP BY version ORDER BY starts DESC LIMIT 20`).bind(start, os, os),
        env.DB.prepare(`SELECT protocol,SUM(total) AS attempts FROM rollups WHERE ${where} AND kind = 'statistics'
            AND protocol <> '' GROUP BY protocol ORDER BY attempts DESC`).bind(...filters)
    ];
    const [daily, versions, byProtocol] = await env.DB.batch(statements);
    return response(200, { daily: daily.results, versions: versions.results, protocols: byProtocol.results });
}

export default {
    async fetch(request, env) {
        const url = new URL(request.url);
        try {
            if (url.pathname === "/v1/reports" && request.method === "POST") return await ingest(request, env);
            if (url.pathname !== "/admin/summary" && url.pathname !== "/admin/data") return response(404);
            if (!(await env.ADMIN_LIMITER.limit({ key: "admin" })).success) return response(429);
            if (!(await hasAccess(request, env))) return response(403);
            if (url.pathname === "/admin/summary" && request.method === "GET") return await summary(url, env);
            if (url.pathname === "/admin/data" && request.method === "DELETE" &&
                request.headers.get("Origin") === env.DASHBOARD_ORIGIN &&
                request.headers.get("X-AppTunnel-Confirm") === "purge") {
                await env.DB.prepare("DELETE FROM rollups").run();
                return response(204);
            }
            return response(405);
        } catch {
            return response(503);
        }
    },

    async scheduled(_event, env) {
        await env.DB.batch([
            env.DB.prepare("DELETE FROM rollups WHERE day < ?").bind(date(Date.now() - 29 * 86400000)),
            env.DB.prepare("DELETE FROM daily_budget WHERE day < ?").bind(date(Date.now() - 86400000))
        ]);
    }
};
