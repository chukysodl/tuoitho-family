const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('mobile dashboard exposes status and only the five authorized remote actions', () => {
  const html = read('remote-dashboard/index.html');
  const js = read('remote-dashboard/app.js');
  for (const label of ['Đã dùng hôm nay', 'Còn lại', 'KHÓA NGAY', 'MỞ KHÓA', '+15 PHÚT', '+30 PHÚT', '+60 PHÚT']) assert.ok(js.includes(label), `missing ${label}`);
  for (const command of ['"lockNow"', '"unlock"', '"grantTime"', '"syncPolicy"']) assert.ok(js.includes(command), `missing command ${command}`);
  assert.match(html, /viewport/);
  assert.match(js, /CHẾ ĐỘ THỬ NGHIỆM – KHÔNG KHÓA WINDOWS THẬT/);
  assert.match(js, /CHẾ ĐỘ THỰC – LỆNH KHÓA CÓ THỂ TÁC ĐỘNG THẬT/);
});

test('remote frontend stores only public project settings persistently and keeps auth in session storage', () => {
  const js = read('remote-dashboard/app.js');
  assert.match(js, /localStorage\.setItem\(configKey/);
  assert.match(js, /sessionStorage\.setItem\(sessionKey/);
  assert.doesNotMatch(js, /localStorage\.setItem\(sessionKey/);
  assert.match(js, /localStorage\.setItem\(configKey, JSON\.stringify\(\{ url, anonKey \}\)\)/);
  assert.match(js, /sb_publishable_/);
  assert.match(js, /role === "anon"/);
  assert.match(js, /service_role|sb_secret_/i);
});

test('mobile setup rejects privileged keys and gives email confirmation guidance', () => {
  const html = read('remote-dashboard/index.html');
  const js = read('remote-dashboard/app.js');
  assert.match(html, /Không nhập secret\/service-role key/);
  assert.match(html, /xác nhận email/);
  assert.match(js, /role === "anon"/);
  assert.match(js, /Chỉ nhập public anon\/publishable key/);
});

test('remote telemetry and UI contain no browsing, page, screenshot, or key logging fields', () => {
  const contracts = read('src/TuoiTho.Core/Remote/RemoteContracts.cs');
  const worker = read('src/TuoiTho.Service/RemoteControlWorker.cs');
  const edge = read('infra/supabase/functions/device-gateway/index.ts');
  for (const forbidden of ['VisitedUrl', 'BrowserHistory', 'Screenshot', 'KeyLog', 'PageText', 'WatchHistory']) {
    assert.ok(!contracts.includes(forbidden), `contract contains ${forbidden}`);
    assert.ok(!worker.includes(forbidden), `worker contains ${forbidden}`);
  }
  assert.match(edge, /const allowed = \["deviceId", "deviceName", "online"/);
  assert.doesNotMatch(edge, /windowTitle|typedContent|pageText|screenshot/i);
  assert.match(edge, /request\.body\?\.getReader\(\)/);
  assert.match(edge, /reader\.cancel\(\)/);
});

test('Supabase database denies direct anonymous and authenticated table access', () => {
  const sql = read('infra/supabase/migrations/202609230001_remote_control.sql');
  assert.match(sql, /enable row level security/);
  assert.match(sql, /revoke all on public\.remote_devices, public\.remote_pairings, public\.remote_commands from anon, authenticated/);
  assert.match(sql, /grant all on public\.remote_devices, public\.remote_pairings, public\.remote_commands to service_role/);
  assert.match(sql, /consumed_at is null/);
});

test('cloud functions enforce owner/device authentication and bounded command actions', () => {
  const parent = read('infra/supabase/functions/parent-gateway/index.ts');
  const device = read('infra/supabase/functions/device-gateway/index.ts');
  assert.match(parent, /auth\.getUser\(\)/);
  assert.match(parent, /\.eq\("owner_id", userData\.user\.id\)/);
  assert.match(parent, /15 \* 60_000/);
  assert.match(device, /credential_sha256/);
  assert.match(device, /x-tuoi-tho-device-credential/);
  assert.match(device, /DEVICE_AUTH_REJECTED/);
  assert.ok(device.indexOf('if (!device) return json({ error: "DEVICE_AUTH_REJECTED" }, 401)') < device.indexOf('if (action === "poll_commands")'));
  assert.match(device, /1_048_576/);
  assert.match(device, /\.is\("acknowledgement", null\)/);
  assert.match(device, /action === "health"/);
  assert.match(device, /from\("remote_devices"\)\.select\("device_id"\)\.limit\(0\)/);
  assert.match(device, /database: "ready"/);
});

test('Supabase gateway JWT settings match the two authentication boundaries', () => {
  const config = read('infra/supabase/config.toml');
  assert.match(config, /\[functions\.device-gateway\][\s\S]*?verify_jwt = false/);
  assert.match(config, /\[functions\.parent-gateway\][\s\S]*?verify_jwt = true/);
});
