const headers = {
  "Content-Type": "application/json; charset=utf-8",
  "Cache-Control": "no-store",
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "POST,OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type"
};

export async function onRequestOptions() {
  return new Response(null, { status: 204, headers });
}

export async function onRequestPost({ request, env }) {
  if (!env?.RIS_IMPORT_URL) {
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

  return json({
    trains: payload.trains,
    version: payload.version || new Date().toISOString()
  });
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers });
}

function readErrorMessage(text) {
  if (!text) return "";

  try {
    const payload = JSON.parse(text);
    return payload.error || payload.detail || payload.message || "";
  } catch {
    return text.slice(0, 180);
  }
}
