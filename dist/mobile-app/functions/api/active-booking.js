const KEY = "active-booking";

const headers = {
  "Content-Type": "application/json; charset=utf-8",
  "Cache-Control": "no-store",
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET,POST,DELETE,OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type"
};

export async function onRequestOptions() {
  return new Response(null, { status: 204, headers });
}

export async function onRequestGet({ env }) {
  const store = getStore(env);
  if (!store) return missingStore();

  const value = await store.get(KEY);
  return json({ active: Boolean(value), booking: value ? JSON.parse(value) : null });
}

export async function onRequestPost({ request, env }) {
  const store = getStore(env);
  if (!store) return missingStore();

  const body = await request.json().catch(() => null);
  const number = String(body?.number || "").trim();
  if (!number) return json({ error: "Zugnummer fehlt." }, 400);

  const booking = {
    number,
    line: String(body?.line || "").trim(),
    role: String(body?.role || "").trim(),
    dateKey: String(body?.dateKey || "").trim(),
    bookedAt: String(body?.bookedAt || new Date().toISOString())
  };

  await store.put(KEY, JSON.stringify(booking));
  return json({ active: true, booking });
}

export async function onRequestDelete({ env }) {
  const store = getStore(env);
  if (!store) return missingStore();

  await store.delete(KEY);
  return json({ active: false, booking: null });
}

function getStore(env) {
  return env?.RIS_STATE || null;
}

function missingStore() {
  return json({
    error: "Cloudflare KV Binding RIS_STATE fehlt.",
    hint: "Lege in Cloudflare Pages eine KV-Bindung mit dem Variablennamen RIS_STATE an."
  }, 503);
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers });
}
