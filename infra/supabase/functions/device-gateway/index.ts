import { createClient } from "npm:@supabase/supabase-js@2";

const cors = { "access-control-allow-origin": "*", "access-control-allow-headers": "authorization, apikey, content-type, x-tuoi-tho-device-id, x-tuoi-tho-device-credential", "access-control-allow-methods": "POST, OPTIONS" };
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { ...cors, "content-type": "application/json" } });
const hex = (bytes: ArrayBuffer) => [...new Uint8Array(bytes)].map(x => x.toString(16).padStart(2, "0")).join("");
const hash = async (value: string) => hex(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
const uuid = (value: unknown): value is string => typeof value === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
const MAX_BODY_BYTES = 1_048_576;
async function readBoundedBody(request: Request): Promise<{ text: string } | { error: "BODY_TOO_LARGE" | "INVALID_ENCODING" }> {
  const contentLength = Number(request.headers.get("content-length") ?? 0);
  if (contentLength > MAX_BODY_BYTES) return { error: "BODY_TOO_LARGE" };
  const reader = request.body?.getReader();
  if (!reader) return { text: "" };
  const chunks: Uint8Array[] = [];
  let size = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > MAX_BODY_BYTES) {
      await reader.cancel();
      return { error: "BODY_TOO_LARGE" };
    }
    chunks.push(value);
  }
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  try { return { text: new TextDecoder("utf-8", { fatal: true }).decode(bytes) }; }
  catch { return { error: "INVALID_ENCODING" }; }
}

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (request.method !== "POST") return json({ error: "METHOD_NOT_ALLOWED" }, 405);
  const body = await readBoundedBody(request);
  if ("error" in body) return json({ error: body.error }, body.error === "BODY_TOO_LARGE" ? 413 : 400);
  let input: Record<string, unknown>;
  try { input = JSON.parse(body.text); } catch { return json({ error: "MALFORMED_JSON" }, 400); }

  const url = Deno.env.get("SUPABASE_URL")!;
  const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
  const db = createClient(url, key, { auth: { persistSession: false, autoRefreshToken: false } });
  const action = input.action;

  if (action === "register_pairing") {
    const p = input.pairing as Record<string, unknown> | undefined;
    if (!p || !uuid(p.deviceId) || typeof p.deviceName !== "string" || p.deviceName.length < 1 || p.deviceName.length > 100 || typeof p.pairCodeSha256 !== "string" || !/^[0-9a-f]{64}$/.test(p.pairCodeSha256) || typeof p.credentialSha256 !== "string" || !/^[0-9a-f]{64}$/.test(p.credentialSha256) || typeof p.expiresAtUtc !== "string") return json({ error: "INVALID_PAIRING" }, 400);
    const expiry = Date.parse(p.expiresAtUtc);
    if (!Number.isFinite(expiry) || expiry <= Date.now() || expiry > Date.now() + 6 * 60_000) return json({ error: "PAIRING_EXPIRED" }, 400);
    const { error } = await db.rpc("register_remote_pairing", { p_device_id: p.deviceId, p_device_name: p.deviceName, p_credential_sha256: p.credentialSha256, p_code_sha256: p.pairCodeSha256, p_expires_at: p.expiresAtUtc });
    if (error) return json({ error: "PAIRING_REGISTER_FAILED" }, 409);
    return json({ accepted: true });
  }

  const deviceId = request.headers.get("x-tuoi-tho-device-id") ?? "";
  const token = request.headers.get("x-tuoi-tho-device-credential") ?? "";
  if (!uuid(deviceId) || token.length < 32 || token.length > 256) return json({ error: "DEVICE_AUTH_REQUIRED" }, 401);
  const credentialHash = await hash(token);
  const { data: device } = await db.from("remote_devices").select("device_id,owner_id").eq("device_id", deviceId).eq("credential_sha256", credentialHash).maybeSingle();
  if (!device) return json({ error: "DEVICE_AUTH_REJECTED" }, 401);

  if (action === "poll_commands") {
    const now = new Date().toISOString();
    const { data, error } = await db.from("remote_commands").select("command_id,device_id,nonce,kind,payload,issued_at,expires_at").eq("device_id", deviceId).is("acknowledgement", null).order("created_at").limit(100);
    if (error) return json({ error: "COMMAND_POLL_FAILED" }, 503);
    await db.from("remote_devices").update({ last_seen_at: now }).eq("device_id", deviceId);
    return json({ commands: (data ?? []).map(c => ({ commandId: c.command_id, deviceId: c.device_id, nonce: c.nonce, timestampUtc: c.issued_at, issuedAtUtc: c.issued_at, expiresAtUtc: c.expires_at, kind: c.kind, payload: c.payload })) });
  }

  if (action === "publish_status") {
    const s = input.status as Record<string, unknown> | undefined;
    const allowed = ["deviceId", "deviceName", "online", "lastSeenAtUtc", "usedSecondsToday", "remainingSeconds", "policyState", "locked", "testMode", "timePolicyRevision", "appPolicyRevision", "webPolicyRevision", "serviceHealth", "policy"];
    if (!s || s.deviceId !== deviceId || Object.keys(s).some(k => !allowed.includes(k))) return json({ error: "INVALID_STATUS" }, 400);
    const now = new Date().toISOString();
    const minimal = Object.fromEntries(allowed.filter(k => k in s).map(k => [k, s[k]]));
    const { error } = await db.from("remote_devices").update({ status: minimal, last_seen_at: now }).eq("device_id", deviceId);
    return error ? json({ error: "STATUS_WRITE_FAILED" }, 503) : json({ accepted: true });
  }

  if (action === "acknowledge") {
    const a = input.acknowledgement as Record<string, unknown> | undefined;
    if (!a || a.deviceId !== deviceId || !uuid(a.commandId) || !["accepted", "rejected", "expired", "duplicate", "applied"].includes(String(a.state)) || typeof a.testMode !== "boolean" || typeof a.acknowledgedAtUtc !== "string" || (a.errorCode !== null && a.errorCode !== undefined && (typeof a.errorCode !== "string" || a.errorCode.length > 128))) return json({ error: "INVALID_ACK" }, 400);
    const { error } = await db.from("remote_commands").update({ acknowledgement: a }).eq("command_id", a.commandId).eq("device_id", deviceId).is("acknowledgement", null);
    return error ? json({ error: "ACK_WRITE_FAILED" }, 503) : json({ accepted: true });
  }
  return json({ error: "UNKNOWN_ACTION" }, 400);
});
