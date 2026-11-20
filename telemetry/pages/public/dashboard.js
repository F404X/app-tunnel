const $ = id => document.getElementById(id);
const filters = $("filters");
const number = new Intl.NumberFormat("pt-BR");
const connections = ["fast", "normal", "slow", "failed"];
const labels = { fast: "Até 5 s", normal: "De 5 s a menos de 20 s", slow: "20 s ou mais", failed: "Falhas" };
let latest = [];
let current;

function table(destination, heading, rows) {
    const parent = $(destination);
    parent.replaceChildren();
    if (rows.length === 0) {
        const empty = document.createElement("p");
        empty.textContent = "Sem dados para estes filtros.";
        parent.append(empty);
        return;
    }
    const result = document.createElement("table");
    const head = result.createTHead().insertRow();
    for (const name of [heading, "Total"]) {
        const cell = document.createElement("th");
        cell.scope = "col";
        cell.textContent = name;
        head.append(cell);
    }
    const body = result.createTBody();
    for (const [name, total] of rows) {
        const row = body.insertRow();
        row.insertCell().textContent = name;
        row.insertCell().textContent = number.format(total);
    }
    parent.append(result);
}

function group(rows) {
    const result = new Map();
    for (const { metric, total } of rows) result.set(metric, (result.get(metric) ?? 0) + total);
    return [...result].sort((a, b) => b[1] - a[1]);
}

function chart(values) {
    const canvas = $("chart");
    const width = canvas.clientWidth;
    if (width === 0) return;
    const density = window.devicePixelRatio || 1;
    canvas.width = Math.round(width * density);
    canvas.height = Math.round(225 * density);
    const ctx = canvas.getContext("2d");
    ctx.scale(density, density);
    ctx.font = "12px system-ui";
    const height = 181;
    const top = 13;
    const left = 47;
    const outer = width - left - 13;
    const max = Math.max(4, ...values.map(day => day.success + day.failed));
    const colors = getComputedStyle(document.documentElement);
    for (let line = 0; line <= 4; line++) {
        const y = top + height * line / 4;
        ctx.strokeStyle = colors.getPropertyValue("--edge").trim();
        ctx.beginPath(); ctx.moveTo(left, y); ctx.lineTo(width - 10, y); ctx.stroke();
        ctx.fillStyle = colors.getPropertyValue("--muted").trim();
        ctx.textAlign = "right";
        ctx.fillText(number.format(Math.round(max * (1 - line / 4))), left - 6, y + 4);
    }
    const slot = outer / Math.max(1, values.length);
    values.forEach((day, index) => {
        const x = left + index * slot + slot * 0.12;
        const success = day.success / max * height;
        const failed = day.failed / max * height;
        ctx.fillStyle = colors.getPropertyValue("--secondary").trim();
        ctx.fillRect(x, top + height - success, slot * 0.76, success);
        ctx.fillStyle = colors.getPropertyValue("--accent").trim();
        ctx.fillRect(x, top + height - success - failed, slot * 0.76, failed);
        if (index % Math.max(1, Math.ceil(values.length / 6)) === 0) {
            ctx.textAlign = "center";
            ctx.fillStyle = colors.getPropertyValue("--muted").trim();
            ctx.fillText(day.label, x + slot * 0.38, 216);
        }
    });
}

function render(data, days) {
    const stats = data.daily.filter(row => row.kind === "statistics");
    const totals = new Map(group(stats));
    const successful = ["fast", "normal", "slow"].reduce((sum, key) => sum + (totals.get(key) ?? 0), 0);
    const failed = totals.get("failed") ?? 0;
    const boxes = [
        ["Aberturas registradas", number.format(totals.get("starts") ?? 0)],
        ["Tentativas de conexão", number.format(successful + failed)],
        ["Rotas aplicadas com sucesso", successful + failed > 0 ? Math.round(successful * 100 / (successful + failed)) + "%" : "—"]
    ];
    $("totals").replaceChildren(...boxes.map(([name, count]) => {
        const element = document.createElement("div");
        const value = document.createElement("strong");
        element.className = "total";
        value.textContent = count;
        element.append(name, value);
        return element;
    }));
    latest = Array.from({ length: days }, (_, i) => {
        const key = new Date(Date.now() - (days - 1 - i) * 86400000).toISOString().slice(0, 10);
        const data = stats.filter(row => row.day === key && connections.includes(row.metric));
        const counted = new Map(group(data));
        return { label: key.slice(8) + "/" + key.slice(5, 7),
            success: ["fast", "normal", "slow"].reduce((sum, k) => sum + (counted.get(k) ?? 0), 0),
            failed: counted.get("failed") ?? 0 };
    });
    chart(latest);
    table("days", "Dia · sucesso e falhas", latest.map(day => [day.label, day.success + day.failed]));
    table("versions", "Versão · aberturas", data.versions.map(row => [row.version, row.starts]));
    table("protocols", "Protocolo · tentativas", data.protocols.map(row => [row.protocol, row.attempts]));
    table("errors", "Etapa / erro", group(data.daily.filter(row => row.kind === "error")));
    table("events", "Eventos de diagnósticos aprovados", group(data.daily.filter(row => row.kind === "diagnostics")));
    table("performance", "Tempo para aplicar a rota", connections.map(key => [labels[key], totals.get(key) ?? 0]));
    const version = filters.elements.version.value;
    const choices = new Set([...(version ? [version] : []), ...data.versions.map(row => row.version)]);
    filters.elements.version.replaceChildren(new Option("Todas", ""), ...[...choices].map(name => new Option(name, name)));
    filters.elements.version.value = version;
}

async function refresh() {
    current?.abort();
    const controller = current = new AbortController();
    $("status").textContent = "Carregando contagens…";
    const query = new URLSearchParams(new FormData(filters));
    try {
        const result = await fetch("/api/summary?" + query, { signal: controller.signal, cache: "no-store" });
        if (!result.ok) throw new Error("summary");
        render(await result.json(), Number(query.get("days")));
        $("status").textContent = "Conexões concluídas em verde e falhas em amarelo. Os filtros de protocolo se aplicam às métricas de conexão.";
    } catch (error) {
        if (error.name !== "AbortError") $("status").textContent = "Não foi possível carregar o painel. Confira o acesso ou tente novamente.";
    }
}

filters.addEventListener("submit", event => { event.preventDefault(); void refresh(); });
new ResizeObserver(() => chart(latest)).observe($("chart"));
$("purge").addEventListener("click", () => { $("confirmation").returnValue = ""; $("confirmation").showModal(); });
$("confirmation").addEventListener("close", async () => {
    if ($("confirmation").returnValue !== "purge") return;
    try {
        const result = await fetch("/api/data", { method: "DELETE", headers: { "X-AppTunnel-Confirm": "purge" } });
        if (!result.ok) throw new Error("delete");
        await refresh();
    } catch {
        $("status").textContent = "A exclusão não foi confirmada.";
    }
});
void refresh();
