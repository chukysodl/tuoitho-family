const $ = (id) => document.getElementById(id);
const configKey = "tuoitho.remote.public-config.v1";
const sessionKey = "tuoitho.remote.auth-session.v1";
let config = null;
let auth = null;
let refreshTimer = null;
let toastTimer = null;
let refreshInFlight = false;
const commandMessages = new Map();

function notify(text) {
  const el = $("toast"); el.textContent = text; el.classList.add("show");
  clearTimeout(toastTimer); toastTimer = setTimeout(() => el.classList.remove("show"), 3200);
}
function isPublicProjectKey(value) {
  if (typeof value !== "string" || value.length < 20 || value.length > 512 || /service_role|sb_secret_/i.test(value)) return false;
  if (value.startsWith("sb_publishable_")) return true;
  const parts = value.split(".");
  if (parts.length !== 3) return false;
  try {
    const payload = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    return JSON.parse(atob(payload + "=".repeat((4 - payload.length % 4) % 4))).role === "anon";
  } catch { return false; }
}
function readConfig() {
  try { config = JSON.parse(localStorage.getItem(configKey) || "null"); } catch { config = null; }
  if (config?.url && isPublicProjectKey(config.anonKey)) config.url = config.url.replace(/\/$/, ""); else config = null;
}
function showSections() {
  $("setup").classList.toggle("hidden", !!config);
  $("auth").classList.toggle("hidden", !config || !!auth);
  $("dashboard").classList.toggle("hidden", !config || !auth);
  if (auth) $("account-label").textContent = auth.user?.email || "Đã đăng nhập";
}
async function api(path, body, token = auth?.access_token) {
  if (!config) throw new Error("Chưa cấu hình kết nối.");
  let response = await fetch(`${config.url}${path}`, { method: "POST", headers: { apikey: config.anonKey, Authorization: `Bearer ${token || config.anonKey}`, "content-type": "application/json" }, body: JSON.stringify(body) });
  if (response.status === 401 && auth?.refresh_token && token === auth.access_token) {
    const refreshed = await authRequest("token?grant_type=refresh_token", { refresh_token: auth.refresh_token });
    saveAuth(refreshed);
    response = await fetch(`${config.url}${path}`, { method: "POST", headers: { apikey: config.anonKey, Authorization: `Bearer ${auth.access_token}`, "content-type": "application/json" }, body: JSON.stringify(body) });
  }
  const result = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(result.error || `Yêu cầu thất bại (${response.status}).`);
  return result;
}
async function authRequest(path, body) {
  const response = await fetch(`${config.url}/auth/v1/${path}`, { method: "POST", headers: { apikey: config.anonKey, "content-type": "application/json" }, body: JSON.stringify(body) });
  const result = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(result.msg || result.message || result.error_description || "Không thể xác thực tài khoản.");
  return result;
}
function saveAuth(value) { auth = value; if (auth) sessionStorage.setItem(sessionKey, JSON.stringify(auth)); else sessionStorage.removeItem(sessionKey); showSections(); }
async function refreshDevices() {
  if (!auth || refreshInFlight || document.activeElement?.tagName === "TEXTAREA" || document.querySelector("details[open]")) return;
  refreshInFlight = true;
  try {
    const result = await api("/functions/v1/parent-gateway", { action: "list_devices" });
    renderDevices(result.devices || []);
  } catch (error) { notify(error.message); }
  finally { refreshInFlight = false; }
}
function renderDevices(devices) {
  const host = $("devices"); host.replaceChildren(); $("empty").classList.toggle("hidden", devices.length > 0);
  for (const device of devices) host.append(makeDeviceCard(device));
}
function makeDeviceCard(device) {
  const status = device.status || {};
  const policy = status.policy || null;
  const lastSeen = Date.parse(device.last_seen_at || status.lastSeenAtUtc || "");
  const online = Number.isFinite(lastSeen) && Date.now() - lastSeen < 45_000;
  const card = document.createElement("article"); card.className = "card"; card.dataset.deviceId = device.device_id;
  const title = document.createElement("div"); title.className = "device-title";
  const name = document.createElement("h2"); name.textContent = device.device_name || "Thiết bị";
  const pill = document.createElement("span"); pill.className = `pill ${online ? "online" : "offline"}`; pill.textContent = online ? "● ONLINE" : "● OFFLINE";
  title.append(name, pill); card.append(title);
  const badge = document.createElement("span"); badge.className = `pill ${status.testMode ? "test" : "real"}`;
  badge.textContent = status.testMode ? "CHẾ ĐỘ THỬ NGHIỆM – KHÔNG KHÓA WINDOWS THẬT" : "CHẾ ĐỘ THỰC – LỆNH KHÓA CÓ THỂ TÁC ĐỘNG THẬT";
  badge.style.background = status.testMode ? "#fff4d6" : "#ffe5e5"; badge.style.color = status.testMode ? "#795b0b" : "#9a2020";
  badge.style.display = "inline-block"; badge.style.marginTop = "10px"; card.append(badge);
  const metrics = document.createElement("div"); metrics.className = "metrics";
  metric(metrics, "Đã dùng hôm nay", formatTime(status.usedSecondsToday));
  metric(metrics, "Còn lại", formatTime(status.remainingSeconds));
  metric(metrics, "Trạng thái", viState(status.policyState));
  metric(metrics, "Lần kết nối", Number.isFinite(lastSeen) ? new Date(lastSeen).toLocaleTimeString("vi-VN") : "—");
  card.append(metrics);
  const actions = document.createElement("div"); actions.className = "actions";
  action(actions, status.testMode ? "KHÓA NGAY · MÔ PHỎNG" : "KHÓA NGAY", "lock", () => sendCommand(device.device_id, "lockNow", {}, card));
  action(actions, "MỞ KHÓA", "unlock", () => sendCommand(device.device_id, "unlock", {}, card));
  action(actions, "+15 PHÚT", "grant", () => sendCommand(device.device_id, "grantTime", { minutes: 15 }, card));
  action(actions, "+30 PHÚT", "grant", () => sendCommand(device.device_id, "grantTime", { minutes: 30 }, card));
  action(actions, "+60 PHÚT", "grant", () => sendCommand(device.device_id, "grantTime", { minutes: 60 }, card));
  card.append(actions);
  const note = document.createElement("p"); note.className = "caption"; note.textContent = "Mở khóa chỉ bỏ Parent Lock; lịch và quota vẫn có hiệu lực. Lệnh hết hạn sau 15 phút nếu máy ngoại tuyến."; card.append(note);

  if (policy) {
    const details = document.createElement("details"); details.className = "policy-editor";
    const summary = document.createElement("summary"); summary.textContent = "Đồng bộ chính sách nâng cao";
    const help = document.createElement("p"); help.textContent = "Chỉnh quota, lịch, quy tắc ứng dụng hoặc website trong JSON chính sách rồi gửi. Bản cục bộ hiện tại được giữ làm điểm bắt đầu.";
    const area = document.createElement("textarea"); area.spellcheck = false; area.value = JSON.stringify(policy, null, 2); area.setAttribute("aria-label", "Chính sách thiết bị dạng JSON");
    const sync = document.createElement("button"); sync.className = "primary"; sync.textContent = "Đồng bộ chính sách"; sync.addEventListener("click", async () => {
      try {
        const snapshot = JSON.parse(area.value);
        if (snapshot.deviceId !== device.device_id || !snapshot.profileId || !Number.isInteger(snapshot.managedSessionId) || !Number.isInteger(snapshot.revision)) throw new Error("Thiếu deviceId/profileId/session/revision hợp lệ.");
        snapshot.revision = Math.max(snapshot.revision + 1, Date.now());
        await sendCommand(device.device_id, "syncPolicy", snapshot, card);
      } catch (error) { notify(`Chính sách chưa được gửi: ${error.message}`); }
    });
    details.append(summary, help, area, sync); card.append(details);
  }
  const commandResult = document.createElement("div"); commandResult.className = "command-result"; commandResult.dataset.result = ""; card.append(commandResult);
  commandResult.textContent = commandMessages.get(device.device_id) || "";
  return card;
}
function metric(parent, title, value) { const box = document.createElement("div"); box.className = "metric"; const label = document.createElement("span"); label.textContent = title; const content = document.createElement("strong"); content.textContent = value; box.append(label, content); parent.append(box); }
function action(parent, text, cls, handler) { const button = document.createElement("button"); button.className = cls; button.textContent = text; button.addEventListener("click", async () => { button.disabled = true; try { await handler(); } finally { button.disabled = false; } }); parent.append(button); }
function formatTime(value) { const seconds = Math.max(0, Math.floor(Number(value) || 0)); return `${String(Math.floor(seconds / 3600)).padStart(2, "0")}:${String(Math.floor(seconds % 3600 / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`; }
function viState(state) { return ({ ALLOWED: "ĐƯỢC PHÉP", OUTSIDE_SCHEDULE: "NGOÀI LỊCH", QUOTA_EXHAUSTED: "HẾT THỜI GIAN", PARENT_LOCK: "KHÓA BỞI PHỤ HUYNH", OVERRIDE: "OVERRIDE" })[state] || "CHƯA RÕ"; }
async function sendCommand(deviceId, kind, payload, card) {
  const output = card.querySelector("[data-result]");
  try {
    setCommandMessage(deviceId, output, "Đang gửi lệnh…");
    const queued = await api("/functions/v1/parent-gateway", { action: "send_command", deviceId, kind, payload });
    setCommandMessage(deviceId, output, "Đã gửi an toàn. Đang chờ máy xác nhận…");
    const deadline = Date.now() + 30_000;
    while (Date.now() < deadline) {
      await new Promise(resolve => setTimeout(resolve, 2500));
      const result = await api("/functions/v1/parent-gateway", { action: "get_command_results", deviceId });
      const found = (result.commands || []).find(item => item.command_id === queued.commandId);
      if (found?.acknowledgement) {
        const ack = found.acknowledgement;
        setCommandMessage(deviceId, output, ack.state === "applied" ? `ĐÃ ÁP DỤNG · ${viState(ack.policyState)} · Còn ${formatTime(ack.remainingSeconds)}` : `TỪ CHỐI · ${ack.errorCode || "Không thực hiện được"}`);
        return;
      }
    }
    setCommandMessage(deviceId, output, "Máy chưa xác nhận. Lệnh có thời hạn; trạng thái sẽ cập nhật khi máy kết nối.");
  } catch (error) { setCommandMessage(deviceId, output, `Không gửi được: ${error.message}`); }
}
function setCommandMessage(deviceId, output, text) { commandMessages.set(deviceId, text); output.textContent = text; }

$("save-config").addEventListener("click", () => {
  const url = $("project-url").value.trim().replace(/\/$/, ""); const anonKey = $("anon-key").value.trim();
  let parsed;
  try { parsed = new URL(url); } catch { return notify("Nhập project URL HTTPS và public anon key hợp lệ."); }
  if (parsed.protocol !== "https:" || parsed.username || parsed.password || parsed.search || parsed.hash || !isPublicProjectKey(anonKey)) return notify("Chỉ nhập public anon/publishable key. Không nhập secret/service-role key.");
  localStorage.setItem(configKey, JSON.stringify({ url, anonKey })); readConfig(); showSections();
});
$("settings-toggle").addEventListener("click", () => { $("setup").classList.toggle("hidden"); $("auth").classList.add("hidden"); });
$("sign-in").addEventListener("click", async () => { try { saveAuth(await authRequest("token?grant_type=password", { email: $("email").value.trim(), password: $("password").value })); notify("Đăng nhập thành công."); await refreshDevices(); } catch (error) { notify(error.message); } });
$("sign-up").addEventListener("click", async () => { try { const result = await authRequest("signup", { email: $("email").value.trim(), password: $("password").value }); if (result.access_token) { saveAuth(result); await refreshDevices(); } else notify("Kiểm tra email để xác nhận tài khoản, sau đó đăng nhập."); } catch (error) { notify(error.message); } });
$("sign-out").addEventListener("click", () => { saveAuth(null); $("devices").replaceChildren(); clearInterval(refreshTimer); });
$("claim-pair").addEventListener("click", async () => { try { await api("/functions/v1/parent-gateway", { action: "claim_pairing", code: $("pair-code").value.trim() }); $("pair-code").value = ""; notify("Đã ghép nối thiết bị."); await refreshDevices(); } catch (error) { notify(error.message); } });

readConfig();
try { auth = JSON.parse(sessionStorage.getItem(sessionKey) || "null"); } catch { auth = null; }
showSections();
if (auth && config) { refreshDevices(); refreshTimer = setInterval(refreshDevices, 8000); }
