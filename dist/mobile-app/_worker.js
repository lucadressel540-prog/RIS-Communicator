const ACTIVE_BOOKING_KEY = "active-booking";
const TRAINS_KEY = "trains";
const DATA_VERSION_KEY = "data-version";

const jsonHeaders = {
  "Content-Type": "application/json; charset=utf-8",
  "Cache-Control": "no-store",
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, DELETE, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type"
};

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname === "/api/active-booking") {
      return handleActiveBooking(request, env);
    }

    if (url.pathname === "/api/trains") {
      return handleTrains(request, env);
    }

    if (url.pathname === "/api/data-version") {
      return handleDataVersion(request, env);
    }

    if (url.pathname === "/api/ris-import") {
      return handleRisImport(request, env);
    }

    return env.ASSETS.fetch(request);
  }
};

async function handleTrains(request, env) {
  if (request.method === "OPTIONS") {
    return new Response(null, { status: 204, headers: jsonHeaders });
  }

  if (request.method !== "GET") {
    return json({ error: "Method not allowed." }, 405);
  }

  if (env.RIS_STATE) {
    const trains = await env.RIS_STATE.get(TRAINS_KEY, { type: "json" });
    if (Array.isArray(trains)) {
      const versionPayload = await env.RIS_STATE.get(DATA_VERSION_KEY, { type: "json" });
      const version = versionPayload?.version || versionPayload || "";
      return json({ trains, version, source: "kv" });
    }
  }

  const assetTrains = await readJsonAsset(request, env, "/data/trains.json");
  const assetVersion = await readJsonAsset(request, env, "/data/data-version.json");
  if (Array.isArray(assetTrains)) {
    return json({
      trains: assetTrains,
      version: assetVersion?.version || "",
      source: "asset"
    });
  }

  return json({ trains: [], version: "", source: "empty" });
}

async function handleDataVersion(request, env) {
  if (request.method === "OPTIONS") {
    return new Response(null, { status: 204, headers: jsonHeaders });
  }

  if (request.method !== "GET") {
    return json({ error: "Method not allowed." }, 405);
  }

  if (env.RIS_STATE) {
    const versionPayload = await env.RIS_STATE.get(DATA_VERSION_KEY, { type: "json" });
    if (versionPayload) {
      return json(typeof versionPayload === "string" ? { version: versionPayload } : versionPayload);
    }
  }

  const assetVersion = await readJsonAsset(request, env, "/data/data-version.json");
  return json(assetVersion || { version: "" });
}

async function handleRisImport(request, env) {
  if (request.method === "OPTIONS") {
    return new Response(null, { status: 204, headers: jsonHeaders });
  }

  if (request.method !== "POST") {
    return json({ error: "Method not allowed." }, 405);
  }

  if (!env.RIS_IMPORT_URL) {
    return json(
      {
        error: "RIS Import ist noch nicht konfiguriert. In Cloudflare fehlt RIS_IMPORT_URL."
      },
      501
    );
  }

  let credentials;
  try {
    credentials = await request.json();
  } catch {
    return json({ error: "Ungueltige Anmeldedaten-Anfrage." }, 400);
  }

  const username = String(credentials?.username || "").trim();
  const password = String(credentials?.password || "");
  if (!username || !password) {
    return json({ error: "Benutzername und Passwort sind erforderlich." }, 400);
  }

  let response;
  try {
    response = await fetch(env.RIS_IMPORT_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username, password })
    });
  } catch {
    return json({ error: "RIS Import Ziel ist nicht erreichbar." }, 502);
  }

  const text = await response.text();
  if (!response.ok) {
    return json(
      {
        error: `RIS Import fehlgeschlagen: ${readErrorMessage(text) || response.status}`
      },
      response.status
    );
  }

  let payload;
  try {
    payload = JSON.parse(text);
  } catch {
    return json({ error: "RIS Import lieferte kein gueltiges JSON." }, 502);
  }

  if (!Array.isArray(payload.trains)) {
    return json({ error: "RIS Import lieferte keine Fahrplanliste." }, 502);
  }

  const version = payload.version || new Date().toISOString();

  if (env.RIS_STATE) {
    try {
      await env.RIS_STATE.put(TRAINS_KEY, JSON.stringify(payload.trains));
      await env.RIS_STATE.put(DATA_VERSION_KEY, JSON.stringify({ version }));
    } catch {
      return json({ error: "RIS Import konnte nicht in KV gespeichert werden." }, 502);
    }
  }

  return json({ trains: payload.trains, version });
}

async function handleActiveBooking(request, env) {
  if (request.method === "OPTIONS") {
    return new Response(null, { status: 204, headers: jsonHeaders });
  }

  if (!env.RIS_STATE) {
    return json(
      {
        error: "Cloudflare KV Binding RIS_STATE fehlt.",
        hint: "Im Pages-Projekt unter Bindungen einen KV-Namensraum mit Variablenname RIS_STATE hinterlegen."
      },
      503
    );
  }

  if (request.method === "GET") {
    const booking = await env.RIS_STATE.get(ACTIVE_BOOKING_KEY, { type: "json" });
    return json({ booking: booking || null });
  }

  if (request.method === "POST") {
    let booking;
    try {
      booking = await request.json();
    } catch {
      return json({ error: "Ungueltiges JSON." }, 400);
    }

    if (!booking || !booking.number) {
      return json({ error: "Buchung ohne Zugnummer." }, 400);
    }

    await env.RIS_STATE.put(
      ACTIVE_BOOKING_KEY,
      JSON.stringify({
        number: String(booking.number),
        line: booking.line ? String(booking.line) : "",
        role: booking.role ? String(booking.role) : "",
        dateKey: booking.dateKey ? String(booking.dateKey) : "today",
        bookedAt: booking.bookedAt || new Date().toISOString()
      })
    );

    return json({ ok: true });
  }

  if (request.method === "DELETE") {
    await env.RIS_STATE.delete(ACTIVE_BOOKING_KEY);
    return json({ ok: true });
  }

  return json({ error: "Method not allowed." }, 405);
}

async function readJsonAsset(request, env, pathname) {
  if (!env.ASSETS) return null;

  try {
    const url = new URL(request.url);
    url.pathname = pathname;
    url.search = "";
    const response = await env.ASSETS.fetch(new Request(url.toString(), request));
    if (!response.ok) return null;
    return await response.json();
  } catch {
    return null;
  }
}

function json(payload, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: jsonHeaders
  });
}

function readErrorMessage(text) {
  if (!text) {
    return "";
  }

  try {
    const payload = JSON.parse(text);
    const main = payload.error || payload.detail || payload.message || "";
    const hint = payload.hint || "";
    return hint ? `${main} ${hint}`.trim() : main;
  } catch {
    const raw = text.slice(0, 180).trim();
    if (/^error code\s*:?\s*\d+/i.test(raw)) {
      return "RIS Import Ziel ist nicht erreichbar. Bitte RIS_IMPORT_URL in Cloudflare pruefen.";
    }
    return raw;
  }
}
