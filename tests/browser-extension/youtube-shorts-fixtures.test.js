const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

class FakeElement {
  constructor({ rect = { left: 0, top: 0, right: 100, bottom: 100, width: 100, height: 100 }, href = null, parent = null, selectors = {}, metaChannelId = null } = {}) {
    this.rect = rect;
    this.href = href;
    this.parent = parent;
    this.selectors = selectors;
    this.metaChannelId = metaChannelId;
    this.isConnected = true;
  }
  getBoundingClientRect() { return this.rect; }
  getAttribute(name) { return name === "href" ? this.href : name === "content" ? this.metaChannelId : null; }
  closest(selector) {
    if (selector.includes("ytd-reel-video-renderer") || selector.includes("ytd-shorts")) return this.parent;
    if (selector.includes("ytd-comments") || selector.includes("ytd-comment") || selector.includes("ytd-compact") || selector.includes("ytd-rich-item") || selector.includes("ytd-reel-shelf")) return null;
    return null;
  }
  querySelector(selector) {
    if (selector.includes("channelId")) return this.metaChannelId ? new FakeElement({ metaChannelId: this.metaChannelId }) : null;
    return this.selectors[selector] || null;
  }
}

const viewport = { left: 0, top: 0, right: 1000, bottom: 800, width: 1000, height: 800 };
const link = href => new FakeElement({ href });
function shortFixture({ ownerHref = null, rect = viewport, active = false, channelId = null, commentHref = null } = {}) {
  const owner = ownerHref ? link(ownerHref) : null;
  const comment = commentHref ? link(commentHref) : null;
  const reel = new FakeElement({ rect, metaChannelId: channelId });
  reel.active = active;
  reel.querySelector = selector => {
    if (selector.includes("channelId")) return channelId ? new FakeElement({ metaChannelId: channelId }) : null;
    if (selector.includes("ytd-reel-player-header-renderer") || selector.includes("#owner") || selector.includes("#channel-name")) return owner;
    if (selector === "a[href]") return comment;
    return null;
  };
  const video = new FakeElement({ rect, parent: reel });
  return { reel, video, comment };
}

let shorts = [];
function installFixture(items) {
  shorts = items;
  global.document = {
    documentElement: { clientWidth: 1000, clientHeight: 800, appendChild() {} },
    querySelectorAll(selector) {
      if (selector.includes("video")) return shorts.map(item => item.video);
      if (selector.includes("ytd-reel-video-renderer") || selector.includes("ytd-shorts")) return shorts.map(item => item.reel);
      return [];
    },
    querySelector(selector) {
      if (selector.includes("[is-active]")) return shorts.find(item => item.reel.active)?.reel || null;
      return null;
    },
    getElementById() { return null; },
    addEventListener() {},
    createElement() { return new FakeElement(); }
  };
}

global.innerWidth = 1000;
global.innerHeight = 800;
global.location = { origin: "https://www.youtube.com", hostname: "www.youtube.com", pathname: "/shorts/one" };
global.history = { back() {} };
global.chrome = { runtime: { sendMessage() {} } };
global.MutationObserver = class { constructor() {} observe() {} };
global.addEventListener = () => {};
global.clearTimeout = () => {};
global.setTimeout = () => 0;
global.HTMLMediaElement = class {};
global.__tuoithoYouTubeTestHooks = true;
installFixture([]);
vm.runInThisContext(fs.readFileSync(path.resolve(__dirname, "../../browser-extension/content/youtube.js"), "utf8"), { filename: "youtube.js" });
const hooks = global.__tuoithoYouTubeTestHooks;

test("finds the centrally visible Short owner without is-active", () => {
  installFixture([shortFixture({ ownerHref: "/@V%E1%BB%8BtB%C3%A9oTV", active: false })]);
  assert.deepEqual(hooks.shortsOwnerIdentity(), { channelHandle: "@VịtBéoTV", channelId: null });
  assert.equal(hooks.content().contentType, "ShortForm");
});

test("ignores an offscreen blocked owner and comment handle for the centered Short", () => {
  const blockedOffscreen = shortFixture({ ownerHref: "/@blocked", rect: { left: 0, top: 1000, right: 1000, bottom: 1800, width: 1000, height: 800 }, commentHref: "/@comment-author" });
  const allowedCentered = shortFixture({ ownerHref: "/@allowed", commentHref: "/@blocked" });
  installFixture([blockedOffscreen, allowedCentered]);
  assert.equal(hooks.shortsOwnerIdentity().channelHandle, "@allowed");
});

test("prefers stable channel id and ignores unrelated generic links", () => {
  installFixture([shortFixture({ ownerHref: "/channel/UCsaygames", channelId: "UCsaygames", commentHref: "/@unrelated" })]);
  assert.deepEqual(hooks.shortsOwnerIdentity(), { channelId: "UCsaygames" });
});

test("swipe reevaluates current owner instead of retaining the previous Short", () => {
  installFixture([shortFixture({ ownerHref: "/@VịtBéoTV" })]);
  assert.equal(hooks.shortsOwnerIdentity().channelHandle, "@VịtBéoTV");
  global.location.pathname = "/shorts/two";
  installFixture([shortFixture({ ownerHref: "/@other" })]);
  assert.equal(hooks.shortsOwnerIdentity().channelHandle, "@other");
});

test("missing scoped owner remains unknown", () => {
  installFixture([shortFixture()]);
  assert.deepEqual(hooks.shortsOwnerIdentity(), {});
});