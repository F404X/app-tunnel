export const protocols = ["wireguard", "wireguard-padding", "amneziawg"];
export const systems = ["windows-10", "windows-11", "other"];
const stages = ["startup", "connect", "disconnect", "recovery", "service"];
const errors = ["timeout", "configuration", "permissions", "io", "windows", "operation", "unexpected"];
const events = ["connected", "disconnected", "handshake-response", "handshake-timeout", "endpoint-failure", "routing-failure", "service-stopped"];
const common = ["schema", "kind", "version", "os"];

export function validVersion(value) {
    return typeof value === "string" && /^(0|[1-9]\d{0,2})\.(0|[1-9]\d{0,2})\.(0|[1-9]\d{0,2})$/.test(value);
}

function exact(value, keys) {
    return value && typeof value === "object" && !Array.isArray(value) &&
        Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
}

function count(value) {
    return Number.isSafeInteger(value) && value >= 0 && value <= 1000;
}

export function validate(value) {
    if (!value || value.schema !== 1 || !validVersion(value.version) || !systems.includes(value.os)) return false;
    if (value.kind === "statistics") {
        if (!exact(value, [...common, "starts", "protocols"]) || !count(value.starts) ||
            !Array.isArray(value.protocols) || value.protocols.length > protocols.length) return false;
        const seen = new Set();
        return value.protocols.every(item => {
            if (!exact(item, ["protocol", "fast", "normal", "slow", "failed"]) || !protocols.includes(item.protocol) ||
                seen.has(item.protocol) || !["fast", "normal", "slow", "failed"].every(key => count(item[key]))) return false;
            seen.add(item.protocol);
            return true;
        });
    }
    if (value.kind === "error") return exact(value, [...common, "stage", "error"]) &&
        stages.includes(value.stage) && errors.includes(value.error);
    if (value.kind === "diagnostics") return exact(value, [...common, "events"]) &&
        Array.isArray(value.events) && value.events.length > 0 && value.events.length <= 24 &&
        value.events.every(entry => events.includes(entry));
    return false;
}

export function dimensions(report) {
    if (report.kind === "error") return [["", report.stage + "/" + report.error, 1]];
    if (report.kind === "diagnostics") {
        const collected = new Map();
        for (const event of report.events) collected.set(event, (collected.get(event) ?? 0) + 1);
        return [...collected].map(([event, amount]) => ["", event, amount]);
    }
    const result = [["", "starts", report.starts]];
    for (const item of report.protocols)
        for (const key of ["fast", "normal", "slow", "failed"]) result.push([item.protocol, key, item[key]]);
    return result.filter(([, , amount]) => amount > 0);
}

export async function readReport(request) {
    if (request.headers.get("Content-Type")?.split(";")[0].trim().toLowerCase() !== "application/json")
        return { status: 415 };
    const limit = 4096;
    if (Number(request.headers.get("Content-Length") ?? 0) > limit) return { status: 413 };
    if (!request.body) return { status: 400 };
    const reader = request.body.getReader();
    const buffer = new Uint8Array(limit);
    let size = 0;
    try {
        for (;;) {
            const { value, done } = await reader.read();
            if (done) break;
            if (size + value.length > limit) {
                await reader.cancel();
                return { status: 413 };
            }
            buffer.set(value, size);
            size += value.length;
        }
        const report = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(buffer.subarray(0, size)));
        return validate(report) ? { status: 202, report } : { status: 400 };
    } catch {
        return { status: 400 };
    } finally {
        reader.releaseLock();
    }
}
