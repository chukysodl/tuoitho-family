import { TEST_MODE } from "./m4-runtime-config.js";

const HOST = "com.tuoitho.browserhost";
const STORAGE_KEY = "tuoithoCustomPolicy";
const OWNED_IDS_KEY = "tuoithoOwnedDnrRuleIds";
const unavailable = diagnostic => ({ allowed: TEST_MODE, reason: "SERVICE_UNAVAILABLE", diagnostic: diagnostic || "Tuổi Thơ chưa kết nối." });

function native(payload) {
  return new Promise(resolve => chrome.runtime.sendNativeMessage(HOST, { ...payload, extensionId: chrome.runtime.id }, result => {
    if (chrome.runtime.lastError) { console.warn("[TuoiTho M4] Native Messaging:", chrome.runtime.lastError.message); resolve(null); return; }
    resolve(result || null);
  }));
}

function hash(text) { let value = 2166136261; for (const ch of text) { value ^= ch.charCodeAt(0); value = Math.imul(value, 16777619); } return value >>> 0; }
function parseRule(rule) {
  if (!rule || rule.provider !== "GenericWeb" || !["Domain", "PathPrefix"].includes(rule.scope) || !["Allow", "Block"].includes(rule.decision)) return null;
  const marker = rule.scope === "Domain" ? "domain:" : "path:";
  if (!String(rule.normalizedKey || "").startsWith(marker)) return null;
  const rest = rule.normalizedKey.slice(marker.length); const slash = rest.indexOf("/");
  const host = slash < 0 ? rest : rest.slice(0, slash); const path = slash < 0 ? "" : rest.slice(slash);
  const validDomain = /^[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?$/i.test(host);
  const validIpv6 = /^\[[0-9a-f:.]+\]$/i.test(host);
  if (!host || (!validDomain && !validIpv6) || (rule.scope === "PathPrefix" && !path.startsWith("/"))) return null;
  return { ...rule, host, path };
}
function ruleFilter(rule) { return `||${rule.host}${rule.scope === "PathPrefix" ? rule.path : "^"}`; }
function dnrRules(rules) {
  const used = new Set();
  return rules.map(parseRule).filter(Boolean).sort((a,b) => a.normalizedKey.localeCompare(b.normalizedKey)).map(rule => {
    let id = 1000000 + (hash(`${rule.scope}|${rule.normalizedKey}`) % 800000000);
    while (used.has(id)) id++; used.add(id);
    const specificity = rule.scope === "PathPrefix" ? 20000 + rule.normalizedKey.length : 10000 + rule.host.length;
    const action = rule.decision === "Allow" ? { type: "allow" } : { type: "redirect", redirect: { extensionPath: `/blocked.html?host=${encodeURIComponent(rule.host)}` } };
    return { id, priority: specificity, action, condition: { urlFilter: ruleFilter(rule), resourceTypes: ["main_frame", "sub_frame"] } };
  });
}
async function applySnapshot(snapshot) {
  const rules = Array.isArray(snapshot.customRules) ? snapshot.customRules : [];
  const generated = dnrRules(rules);
  const storage = await chrome.storage.local.get([OWNED_IDS_KEY]);
  const removeRuleIds = Array.isArray(storage[OWNED_IDS_KEY]) ? storage[OWNED_IDS_KEY] : [];
  await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds, addRules: generated });
  await chrome.storage.local.set({ [OWNED_IDS_KEY]: generated.map(rule => rule.id), [STORAGE_KEY]: { revision: snapshot.policyRevision || 0, rules } });
  return generated.length;
}
async function syncPolicy() {
  const result = await native({ isPolicySync: true, provider: "GenericWeb", host: "", path: "", contentType: "Unknown" });
  if (!result || result.reason !== "POLICY_SNAPSHOT") return false;
  const existing = await chrome.storage.local.get([STORAGE_KEY]);
  if ((existing[STORAGE_KEY]?.revision ?? -1) !== (result.policyRevision ?? 0)) await applySnapshot(result);
  return true;
}

chrome.runtime.onInstalled.addListener(() => { void syncPolicy(); });
chrome.runtime.onStartup.addListener(() => { void syncPolicy(); });
chrome.webNavigation.onCommitted.addListener(details => { if (details.frameId === 0) void syncPolicy(); }, { url: [{ schemes: ["http", "https"] }] });

chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (!message || message.type !== "tuoitho-navigation" || !sender.tab) return;
  native(message.payload).then(result => reply(result || unavailable("BrowserHost không phản hồi.")));
  return true;
});

export const __test = { parseRule, dnrRules };