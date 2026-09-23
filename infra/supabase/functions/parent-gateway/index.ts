import { createClient } from "npm:@supabase/supabase-js@2";

const cors = { "access-control-allow-origin": "*", "access-control-allow-headers": "authorization, apikey, content-type", "access-control-allow-methods": "POST, OPTIONS" };
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { ...cors, "content-type": "application/json" } });
const hex = (bytes: ArrayBuffer | Uint8Array) => [...(bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes))].map(x => x.toString(16).padStart(2, "0")).join("");
const hashCode = async (value: string) => hex(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value.replaceAll("-", "").toUpperCase())));
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
  const authorization = request.headers.get("authorization");
  if (!authorization?.startsWith("Bearer ")) return json({ error: "AUTH_REQUIRED" }, 401);
  const url = Deno.env.get("SUPABASE_URL")!;
  const anon = Deno.env.get("SUPABASE_ANON_KEY")!;
  const service = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
  const authClient = createClient(url, anon, { global: { headers: { Authorization: authorization } }, auth: { persistSession: false } });
  const { data: userData, error: authError } = await authClient.auth.getUser();
  if (authError || !userData.user) return json({ error: "AUTH_REJECTED" }, 401);
  const db = createClient(url, service, { auth: { persistSession: false, autoRefreshToken: false } });

  if (input.action === "claim_pairing") {
    const code = typeof input.code === "string" ? input.code.trim() : "";
    if (code.length < 16 || code.length > 32) return json({ error: "INVALID_PAIRING_CODE" }, 400);
    const { data, error } = await db.rpc("claim_remote_pairing", { p_code_sha256: await hashCode(code), p_owner_id: userData.user.id });
    return error ? json({ error: "PAIRING_INVALID_EXPIRED_OR_USED" }, 409) : json({ deviceId: data });
  }

  if (input.action === "list_devices") {
    const { data, error } = await db.from("remote_devices").select("device_id,device_name,status,last_seen_at").eq("owner_id", userData.user.id).order("device_name");
    return error ? json({ error: "DEVICE_LIST_FAILED" }, 503) : json({ devices: data ?? [] });
  }

  if (input.action === "send_command") {
    const deviceId = input.deviceId;
    const kind = input.kind;
    if (!uuid(deviceId) || !["lockNow", "unlock", "grantTime", "syncPolicy"].includes(String(kind))) return json({ error: "INVALID_COMMAND" }, 400);
    const { data: device } = await db.from("remote_devices").select("device_id").eq("device_id", deviceId).eq("owner_id", userData.user.id).maybeSingle();
    if (!device) return json({ error: "DEVICE_NOT_OWNED" }, 404);
    const payload = input.payload && typeof input.payload === "object" && !Array.isArray(input.payload) ? input.payload : {};
    if (["lockNow", "unlock"].includes(String(kind)) && Object.keys(payload as object).length !== 0) return json({ error: "INVALID_PAYLOAD" }, 400);
    if (kind === "grantTime" && ![15, 30, 60].includes(Number((payload as Record<string, unknown>).minutes))) return json({ error: "INVALID_GRANT" }, 400);
    const now = new Date();
    const command = { command_id: crypto.randomUUID(), device_id: deviceId, nonce: hex(crypto.getRandomValues(new Uint8Array(24))), kind, payload, issued_at: now.toISOString(), expires_at: new Date(now.getTime() + 15 * 60_000).toISOString() };
    const { error } = await db.from("remote_commands").insert(command);
    return error ? json({ error: "COMMAND_QUEUE_FAILED" }, 503) : json({ accepted: true, commandId: command.command_id, expiresAtUtc: command.expires_at });
  }

  if (input.action === "get_command_results") {
    const deviceId = input.deviceId;
    if (!uuid(deviceId)) return json({ error: "INVALID_DEVICE" }, 400);
    const { data: device } = await db.from("remote_devices").select("device_id").eq("device_id", deviceId).eq("owner_id", userData.user.id).maybeSingle();
    if (!device) return json({ error: "DEVICE_NOT_OWNED" }, 404);
    const { data, error } = await db.from("remote_commands").select("command_id,kind,issued_at,expires_at,acknowledgement").eq("device_id", deviceId).order("created_at", { ascending: false }).limit(20);
    return error ? json({ error: "COMMAND_RESULTS_FAILED" }, 503) : json({ commands: data ?? [] });
  }
  return json({ error: "UNKNOWN_ACTION" }, 400);
});
