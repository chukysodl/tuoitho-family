const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const viewport = { left: 0, top: 0, right: 1000, bottom: 800, width: 1000, height: 800 };
class FakeElement {
  constructor({ tagName = "DIV", rect = viewport, href = null, textContent = "", reel = null, surface = null, region = null, excluded = false, channelId = null, parentElement = null } = {}) {
    Object.assign(this, { tagName, rect, href, textContent, reel, surface, region, excluded, channelId, parentElement, isConnected: true, anchors: [], textRegions: [], videos: [] });
  }
  getBoundingClientRect() { return this.rect; }
  getAttribute(name) { return name === "href" ? this.href : name === "content" ? this.channelId : null; }
  contains(element) { for (let current = element; current; current = current.parentElement) if (current === this) return true; return false; }
  closest(selector) {
    if (selector.includes("ytd-comments") || selector.includes("ytd-comment") || selector.includes("ytd-compact") || selector.includes("ytd-rich-item") || selector.includes("ytd-reel-shelf") || selector.includes("ytd-shorts-remix")) return this.excluded ? this : null;
    if (selector.includes("ytd-reel-video-renderer") || selector.includes("ytd-shorts-player")) return this.reel;
    if (selector.includes("ytd-shorts")) return this.surface;
    if (selector.includes("ytd-reel-player-header-renderer") || selector.includes("ytd-reel-player-overlay-renderer") || selector.includes("ytd-reel-player-metadata-renderer") || selector.includes("data-shorts-owner") || selector.includes("#owner") || selector.includes("#channel-name") || selector.includes("ytd-channel-name")) return this.region;
    return null;
  }
  querySelector(selector) {
    if (selector.includes("channelId")) return this.channelId ? new FakeElement({ channelId: this.channelId }) : null;
    return this.querySelectorAll(selector)[0] || null;
  }
  querySelectorAll(selector) {
    if (selector.includes("video")) return this.videos;
    if (selector.includes("a[href")) return this.anchors;
    if (selector.includes("ytd-reel-player-header-renderer") || selector.includes("ytd-reel-player-overlay-renderer") || selector.includes("ytd-reel-player-metadata-renderer") || selector.includes("data-shorts-owner") || selector.includes("#owner") || selector.includes("#channel-name") || selector.includes("ytd-channel-name")) return this.textRegions;
    return [];
  }
}
const handleLink = (href, options = {}) => new FakeElement({ tagName: "A", href, ...options });
const textRegion = (textContent, options = {}) => new FakeElement({ tagName: "YT-CHANNEL-NAME", textContent, ...options });

function shortFixture({ rect = viewport, active = false, ownerHref = null, channelId = null, parentElement = null } = {}) {
  const surface = new FakeElement({ tagName: "YTD-SHORTS", rect });
  const reel = new FakeElement({ tagName: "YTD-REEL-VIDEO-RENDERER", rect, surface, channelId });
  reel.active = active;
  reel.parentElement = surface;
  const video = new FakeElement({ tagName: "VIDEO", rect, reel, surface });
  video.parentElement = reel;
  reel.videos = [video];
  surface.videos = [video];
  if (ownerHref) {
    const owner = handleLink(ownerHref, { reel, surface, region: reel, parentElement: reel });
    reel.anchors = [owner]; surface.anchors = [owner];
  }
  return { surface, reel, video };
}
function addSiblingOverlay(fixture, href = null, text = null, options = {}) {
  const overlay = new FakeElement({ tagName: "YTD-REEL-PLAYER-OVERLAY-RENDERER", rect: options.rect || fixture.reel.rect, surface: fixture.surface });
  overlay.parentElement = fixture.surface;
  if (href) {
    const anchor = handleLink(href, { surface: fixture.surface, region: overlay, parentElement: overlay, ...options });
    fixture.surface.anchors.push(anchor); overlay.anchors.push(anchor);
  }
  if (text) {
    const region = textRegion(text, { surface: fixture.surface, region: overlay, parentElement: overlay, ...options });
    fixture.surface.textRegions.push(region); overlay.textRegions.push(region);
  }
  return overlay;
}

let shorts = [];
function installFixture(items) {
  shorts = items;
  const surfaces = [...new Set(items.map(item => item.surface))];
  global.document = {
    documentElement: { clientWidth: 1000, clientHeight: 800, appendChild() {} },
    querySelectorAll(selector) {
      if (selector.includes("video")) return items.map(item => item.video);
      if (selector.includes("ytd-reel-video-renderer") || selector.includes("ytd-shorts")) return items.map(item => item.reel);
      return [];
    },
    querySelector(selector) {
      if (selector.includes("[is-active]")) return items.find(item => item.reel.active)?.reel || null;
      if (selector.includes("playables")) return null;
      return null;
    },
    getElementById() { return null; }, addEventListener() {}, createElement() { return new FakeElement(); }
  };
  for (const surface of surfaces) surface.parentElement = null;
}

global.innerWidth = 1000; global.innerHeight = 800;
global.location = { origin: "https://www.youtube.com", hostname: "www.youtube.com", pathname: "/shorts/one" };
global.history = { back() {} }; global.chrome = { runtime: { sendMessage() {} } };
global.MutationObserver = class { observe() {} }; global.addEventListener = () => {}; global.clearTimeout = () => {}; global.setTimeout = () => 0; global.HTMLMediaElement = class {};
global.__tuoithoYouTubeTestHooks = true; installFixture([]);
vm.runInThisContext(fs.readFileSync(path.resolve(__dirname, "../../browser-extension/content/youtube.js"), "utf8"), { filename: "youtube.js" });
const hooks = global.__tuoithoYouTubeTestHooks;

test("finds Unicode owner anchor directly inside current reel without is-active", () => {
  installFixture([shortFixture({ ownerHref: "/@V%E1%BB%8BtB%C3%A9oTV" })]);
  const found = hooks.shortsOwnerResolution();
  assert.deepEqual(found.identity, { channelHandle: "@VịtBéoTV", channelId: null });
  assert.equal(found.diagnostics.ownerSource, "ANCHOR");
});

test("finds a current owner anchor in a geometrically associated sibling overlay", () => {
  const current = shortFixture(); addSiblingOverlay(current, "/@VịtBéoTV"); installFixture([current]);
  const found = hooks.shortsOwnerResolution();
  assert.equal(found.identity.channelHandle, "@VịtBéoTV");
  assert.equal(found.diagnostics.shortContainer, "ytd-reel-video-renderer");
});

test("uses literal visible handle text only from scoped owner metadata", () => {
  const current = shortFixture(); addSiblingOverlay(current, null, "@VịtBéoTV · 1,2 Tr người đăng ký"); installFixture([current]);
  const found = hooks.shortsOwnerResolution();
  assert.equal(found.identity.channelHandle, "@VịtBéoTV");
  assert.equal(found.diagnostics.ownerSource, "VISIBLE_HANDLE_TEXT");
});

test("comments and offscreen blocked reels cannot win over the current owner", () => {
  const offscreen = shortFixture({ ownerHref: "/@blocked", rect: { left: 0, top: 1000, right: 1000, bottom: 1800, width: 1000, height: 800 } });
  const current = shortFixture({ ownerHref: "/@allowed" });
  const comment = handleLink("/@blocked", { reel: current.reel, surface: current.surface, excluded: true, rect: viewport });
  current.reel.anchors.push(comment); current.surface.anchors.push(comment);
  installFixture([offscreen, current]);
  assert.equal(hooks.shortsOwnerResolution().identity.channelHandle, "@allowed");
});

test("remix creator is rejected and does not override current owner", () => {
  const current = shortFixture({ ownerHref: "/@allowed" });
  addSiblingOverlay(current, "/@remix", null, { excluded: true }); installFixture([current]);
  assert.equal(hooks.shortsOwnerResolution().identity.channelHandle, "@allowed");
});

test("channel id is a trusted owner source", () => {
  installFixture([shortFixture({ ownerHref: "/channel/UCsaygames", channelId: "UCsaygames" })]);
  const found = hooks.shortsOwnerResolution();
  assert.equal(found.identity.channelId, "UCsaygames");
  assert.equal(found.diagnostics.ownerSource, "CHANNEL_ID");
});

test("ambiguous visible owners fail closed to unknown", () => {
  const current = shortFixture({ ownerHref: "/@one" });
  const second = handleLink("/@two", { reel: current.reel, surface: current.surface, region: current.reel, parentElement: current.reel });
  current.reel.anchors.push(second); current.surface.anchors.push(second); installFixture([current]);
  const found = hooks.shortsOwnerResolution();
  assert.deepEqual(found.identity, {}); assert.equal(found.diagnostics.ownerSource, "NONE"); assert.equal(found.diagnostics.ownerCandidateCount, 2);
});

test("swipe recomputes owner rather than retaining the previous Short", () => {
  installFixture([shortFixture({ ownerHref: "/@VịtBéoTV" })]); assert.equal(hooks.shortsOwnerIdentity().channelHandle, "@VịtBéoTV");
  global.location.pathname = "/shorts/two"; installFixture([shortFixture({ ownerHref: "/@other" })]); assert.equal(hooks.shortsOwnerIdentity().channelHandle, "@other");
});