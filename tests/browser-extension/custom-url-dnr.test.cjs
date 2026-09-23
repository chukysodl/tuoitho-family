const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { pathToFileURL } = require("node:url");

let registered = [];
let stored = {};
let dynamicRules = [];
let shouldFailDnrUpdate = false;
let diagnostics = [];
global.chrome = {
  runtime: {
    id: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    onMessage: { addListener: fn => registered.push(fn) }, onInstalled: { addListener: () => {} }, onStartup: { addListener: () => {} },
    sendNativeMessage: (_host, payload, callback) => { diagnostics.push(payload); callback({ allowed: true }); }
  },
  webNavigation: { onCommitted: { addListener: () => {} } },
  storage: { local: {
    get: async () => stored,
    set: async values => { stored = { ...stored, ...values }; }
  } },
  declarativeNetRequest: {
    updateDynamicRules: async ({ removeRuleIds, addRules }) => {
      if (shouldFailDnrUpdate) throw new Error("https://secret.invalid/?token=hidden");
      const remove = new Set(removeRuleIds); dynamicRules = dynamicRules.filter(rule => !remove.has(rule.id)).concat(addRules);
    },
    getDynamicRules: async () => dynamicRules
  }
};async function worker() {
  const root = path.resolve(__dirname, "..", "..", "browser-extension");
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "tuoitho-dnr-"));
  fs.copyFileSync(path.join(root, "service-worker.js"), path.join(temp, "service-worker.mjs"));
  fs.copyFileSync(path.join(root, "m4-runtime-config.js"), path.join(temp, "m4-runtime-config.js"));
  return import(pathToFileURL(path.join(temp, "service-worker.mjs")).href + `?${Date.now()}`);
}
test("deterministic DNR rules cover frames, preserve exceptions, and never use arbitrary resources", async () => {
  const { __test } = await worker();
  const rules = __test.dnrRules([
    { provider: "GenericWeb", scope: "Domain", decision: "Block", normalizedKey: "domain:example.com" },
    { provider: "GenericWeb", scope: "PathPrefix", decision: "Allow", normalizedKey: "path:example.com/learning/" },
    { provider: "GenericWeb", scope: "PathPrefix", decision: "Block", normalizedKey: "path:poki.com/games/" }
  ]);
  assert.equal(rules.length, 3);
  assert.deepEqual(rules.map(rule => rule.condition.resourceTypes), [["main_frame", "sub_frame"], ["main_frame", "sub_frame"], ["main_frame", "sub_frame"]]);
  assert.ok(rules.some(rule => rule.action.type === "allow" && rule.priority > 10000));
  assert.ok(rules.some(rule => rule.condition.urlFilter === "||example.com^"));
  assert.ok(rules.every(rule => Number.isInteger(rule.id) && rule.id >= 1000000));
  assert.equal(new Set(rules.map(rule => rule.id)).size, rules.length);
});
test("invalid or protected values do not create DNR rules", async () => {
  const { __test } = await worker();
  assert.deepEqual(__test.dnrRules([{ provider: "GenericWeb", scope: "Domain", decision: "Block", normalizedKey: "domain:chrome://settings" }]), []);
});test("DNR update errors retain active rules and emit a URL-free TestMode diagnostic", async () => {
  stored = {}; dynamicRules = []; diagnostics = []; shouldFailDnrUpdate = false;
  const { __test } = await worker();
  const first = await __test.applySnapshot({ policyRevision: 1, customRules: [{ provider: "GenericWeb", scope: "Domain", decision: "Block", normalizedKey: "domain:snapsave.gg" }] });
  assert.equal(first.ok, true); assert.equal(dynamicRules.length, 1);
  shouldFailDnrUpdate = true;
  const failed = await __test.applySnapshot({ policyRevision: 2, customRules: [{ provider: "GenericWeb", scope: "Domain", decision: "Block", normalizedKey: "domain:other.example" }] });
  assert.equal(failed.ok, false); assert.equal(dynamicRules.length, 1);
  const diagnostic = diagnostics.at(-1);
  assert.equal(diagnostic.dnrSyncState, "FAIL");
  assert.ok(!diagnostic.dnrError.includes("https://"));
});