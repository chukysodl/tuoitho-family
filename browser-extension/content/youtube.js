(() => {
  const provider = "YouTube";
  const overlayId = "tuoitho-youtube-block";
  const bannerId = "tuoitho-service-unavailable";
  let retry = 0;
  let retryTimer = null;
  let blocked = false;

  const decode = value => { try { return decodeURIComponent(value).normalize("NFC"); } catch { return null; } };
  const identityFromHref = href => {
    if (!href) return {};
    try {
      const path = new URL(href, location.origin).pathname.split("/").filter(Boolean);
      if (!path.length) return {};
      if (path[0].startsWith("@")) return { channelHandle: decode(path[0]) };
      if (path[0] === "channel" && /^UC[\w-]{2,}$/.test(path[1] || "")) return { channelId: path[1] };
      if ((path[0] === "c" || path[0] === "user") && path[1]) return { channelHandle: `@${decode(path[1])}` };
    } catch { }
    return {};
  };
  const channelIdFrom = root => {
    const value = root?.querySelector("link[itemprop='channelId'], meta[itemprop='channelId']")?.getAttribute("content");
    return value && /^UC[\w-]{2,}$/.test(value) ? value : null;
  };
  const channelPageIdentity = () => ({ ...identityFromHref(location.pathname), channelId: channelIdFrom(document) || identityFromHref(location.pathname).channelId || null });
  const ownerIdentity = () => {
    const selectors = [
      "ytd-video-owner-renderer #channel-name a[href]",
      "ytd-video-owner-renderer a[href*='/channel/']",
      "ytd-watch-metadata ytd-channel-name a[href]",
      "#owner ytd-channel-name a[href]"
    ];
    for (const selector of selectors) {
      const link = document.querySelector(selector);
      const identity = identityFromHref(link?.getAttribute("href"));
      if (identity.channelHandle || identity.channelId) return { ...identity, channelId: channelIdFrom(link?.closest("ytd-video-owner-renderer")) || identity.channelId || null };
    }
    return {};
  };
  const viewportScore = element => {
    if (!element || element.isConnected === false || typeof element.getBoundingClientRect !== "function") return Number.NEGATIVE_INFINITY;
    const rect = element.getBoundingClientRect();
    const width = globalThis.innerWidth || document.documentElement?.clientWidth || 1;
    const height = globalThis.innerHeight || document.documentElement?.clientHeight || 1;
    const visibleWidth = Math.max(0, Math.min(rect.right, width) - Math.max(rect.left, 0));
    const visibleHeight = Math.max(0, Math.min(rect.bottom, height) - Math.max(rect.top, 0));
    const visibleArea = visibleWidth * visibleHeight;
    const area = Math.max(1, rect.width * rect.height);
    if (visibleArea / area < 0.2) return Number.NEGATIVE_INFINITY;
    const distance = Math.abs((rect.left + rect.right) / 2 - width / 2) + Math.abs((rect.top + rect.bottom) / 2 - height / 2);
    return visibleArea - distance;
  };
  const shortContainerFor = element => element?.closest?.("ytd-reel-video-renderer, ytd-shorts, ytd-shorts-player") || null;
  const bestVisible = candidates => candidates
    .map(item => ({ item, score: viewportScore(item) }))
    .filter(candidate => Number.isFinite(candidate.score))
    .sort((left, right) => right.score - left.score)[0]?.item || null;
  const currentShortContext = () => {
    const videos = [...document.querySelectorAll("ytd-reel-video-renderer video, ytd-shorts video")]
      .map(video => ({ video, container: shortContainerFor(video) }))
      .filter(candidate => candidate.container);
    const activeVideo = bestVisible(videos.map(candidate => candidate.video));
    const fromVideo = shortContainerFor(activeVideo);
    if (fromVideo) return { video: activeVideo, container: fromVideo, surface: fromVideo.closest?.("ytd-shorts") || fromVideo.parentElement || fromVideo };

    const active = document.querySelector("ytd-reel-video-renderer[is-active], ytd-reel-video-renderer[is-active='true']");
    const container = active || bestVisible([...document.querySelectorAll("ytd-reel-video-renderer, ytd-shorts")]);
    if (!container) return null;
    return { video: bestVisible([...container.querySelectorAll?.("video")] || []), container, surface: container.closest?.("ytd-shorts") || container.parentElement || container };
  };
  const currentShortContainer = () => currentShortContext()?.container || null;
  const excludedShortContext = "ytd-comments, ytd-comment-thread-renderer, ytd-comment-view-model, ytd-compact-video-renderer, ytd-rich-item-renderer, ytd-reel-shelf-renderer, ytd-shorts-remix-renderer";
  const shortOwnerRegions = "ytd-reel-player-header-renderer, ytd-reel-player-overlay-renderer, ytd-reel-player-metadata-renderer, [data-shorts-owner], #owner, #channel-name, ytd-channel-name";
  const distanceBetween = (left, right) => {
    if (!left || !right || typeof left.getBoundingClientRect !== "function" || typeof right.getBoundingClientRect !== "function") return Number.POSITIVE_INFINITY;
    const a = left.getBoundingClientRect();
    const b = right.getBoundingClientRect();
    return Math.abs((a.left + a.right) / 2 - (b.left + b.right) / 2) + Math.abs((a.top + a.bottom) / 2 - (b.top + b.bottom) / 2);
  };
  const candidateBelongsToCurrentShort = (candidate, context) => {
    if (!candidate || candidate.isConnected === false || candidate.closest?.(excludedShortContext)) return false;
    if (!Number.isFinite(viewportScore(candidate))) return false;
    const reel = shortContainerFor(candidate);
    if (reel && reel !== context.container) return false;
    if (reel === context.container || context.container.contains?.(candidate)) return true;
    const region = candidate.closest?.(shortOwnerRegions);
    return Boolean(region && context.surface?.contains?.(candidate) && distanceBetween(candidate, context.video) < (globalThis.innerHeight || 800) * 0.9);
  };
  const handleIdentityFromText = element => {
    const visibleText = (element?.textContent || "").trim().normalize("NFC");
    const match = visibleText.match(/^(@[\p{L}\p{N}._-]{1,100})(?:\s|$)/u);
    return match ? { channelHandle: match[1] } : {};
  };
  const addCandidate = (candidates, identity, source, element, context, bonus = 0) => {
    if (!identity.channelHandle && !identity.channelId) return;
    const candidate = element ? viewportScore(element) - distanceBetween(element, context.video) + bonus : 1_000_000 + bonus;
    if (!Number.isFinite(candidate)) return;
    candidates.push({ identity: { ...identity, channelId: channelIdFrom(context.container) || identity.channelId || null }, source, score: candidate });
  };
  const shortsOwnerResolution = () => {
    const context = currentShortContext();
    const diagnostics = { shortContainer: context?.container?.tagName?.toLowerCase?.() || null, ownerCandidateCount: 0, ownerSource: "NONE" };
    if (!context) return { identity: {}, diagnostics };

    const candidates = [];
    const channelId = channelIdFrom(context.container);
    if (channelId) addCandidate(candidates, { channelId }, "CHANNEL_ID", null, context, 10_000);
    const anchorSelector = "a[href^='/@'], a[href*='youtube.com/@'], a[href^='/channel/']";
    const roots = [...new Set([context.container, context.surface].filter(Boolean))];
    for (const root of roots) for (const link of root.querySelectorAll?.(anchorSelector) || []) {
      if (!candidateBelongsToCurrentShort(link, context)) continue;
      addCandidate(candidates, identityFromHref(link.getAttribute("href")), "ANCHOR", link, context, root === context.container ? 1_000 : 0);
    }
    if (!candidates.length) for (const root of roots) for (const element of root.querySelectorAll?.(shortOwnerRegions) || []) {
      if (!candidateBelongsToCurrentShort(element, context)) continue;
      addCandidate(candidates, handleIdentityFromText(element), "VISIBLE_HANDLE_TEXT", element, context, root === context.container ? 500 : 0);
    }

    const unique = new Map();
    for (const candidate of candidates) {
      const key = candidate.identity.channelId ? `id:${candidate.identity.channelId}` : `handle:${candidate.identity.channelHandle.toLowerCase()}`;
      if (!unique.has(key) || unique.get(key).score < candidate.score) unique.set(key, candidate);
    }
    const ranked = [...unique.values()].sort((left, right) => right.score - left.score);
    diagnostics.ownerCandidateCount = ranked.length;
    if (!ranked.length || (ranked.length > 1 && ranked[0].score - ranked[1].score < 250)) return { identity: {}, diagnostics };
    diagnostics.ownerSource = ranked[0].source;
    return { identity: ranked[0].identity, diagnostics };
  };
  const shortsOwnerIdentity = () => shortsOwnerResolution().identity;
  const playableOwnerIdentity = () => {
    const root = document.querySelector("ytd-playables-player-page-renderer, ytd-playables-renderer, ytd-playables-game-renderer, [data-playables-player]");
    if (!root) return {};
    const selectors = [
      "[data-playables-publisher] a[href]",
      "ytd-video-owner-renderer #channel-name a[href]",
      "#publisher ytd-channel-name a[href]",
      "#owner ytd-channel-name a[href]"
    ];
    for (const selector of selectors) {
      const link = root.querySelector(selector);
      const identity = identityFromHref(link?.getAttribute("href"));
      if (identity.channelHandle || identity.channelId) return { ...identity, channelId: channelIdFrom(root) || identity.channelId || null };
    }
    return {};
  };
  const pauseMedia = () => document.querySelectorAll("video,audio").forEach(media => { try { media.pause(); } catch { } });
  const remove = id => document.getElementById(id)?.remove();
  const unblock = () => { blocked = false; remove(overlayId); };
  const block = identity => {
    blocked = true;
    pauseMedia();
    if (document.getElementById(overlayId)) return;
    const box = document.createElement("section");
    box.id = overlayId;
    box.setAttribute("role", "dialog");
    box.setAttribute("aria-modal", "true");
    box.innerHTML = `<div><h1>Nội dung này chưa được phụ huynh cho phép.</h1><p>YouTube</p>${identity.channelHandle ? `<p>Kênh: ${identity.channelHandle}</p>` : ""}<button type="button">Quay lại</button></div>`;
    Object.assign(box.style, { position: "fixed", inset: "0", zIndex: "2147483647", display: "grid", placeItems: "center", background: "#11233ddd", color: "white", font: "20px Segoe UI,sans-serif", textAlign: "center", pointerEvents: "auto" });
    Object.assign(box.firstElementChild.style, { maxWidth: "38rem", padding: "2.5rem", borderRadius: "1rem", background: "#173b64" });
    box.querySelector("button").onclick = () => history.back();
    document.documentElement.appendChild(box);
  };
  const serviceBanner = visible => {
    if (!visible) return remove(bannerId);
    if (document.getElementById(bannerId)) return;
    const banner = document.createElement("aside");
    banner.id = bannerId;
    banner.textContent = "Tuổi Thơ chưa kết nối — kiểm soát Web đang tạm ngưng.";
    Object.assign(banner.style, { position: "fixed", top: "12px", right: "12px", zIndex: "2147483646", padding: "10px 14px", borderRadius: "8px", background: "#fff3cd", color: "#5f4500", font: "14px Segoe UI,sans-serif" });
    document.documentElement.appendChild(banner);
  };
  const content = () => {
    const path = location.pathname;
    if (/^\/(?:@|channel\/|c\/|user\/)/i.test(path)) return { contentType: "Channel", ...channelPageIdentity() };
    if (path.startsWith("/shorts/")) { const short = shortsOwnerResolution(); return { contentType: "ShortForm", ...short.identity, ...short.diagnostics }; }
    if (path.startsWith("/playables/")) return { contentType: "Playable", ...playableOwnerIdentity() };
    if (path === "/watch") return { contentType: "Video", ...ownerIdentity() };
    return { contentType: "Site" };
  };
  const evaluate = () => {
    clearTimeout(retryTimer);
    const identity = content();
    const ownerState = identity.contentType === "ShortForm" ? (identity.channelHandle || identity.channelId ? "SHORT_OWNER_FOUND" : "SHORT_OWNER_UNKNOWN") : null;
    chrome.runtime.sendMessage({ type: "tuoitho-navigation", payload: { profileId: "m1-child", managedSessionId: 0, provider, host: location.hostname, path: location.pathname, ...identity, ownerState } }, response => {
      const result = response || { allowed: true, reason: "SERVICE_UNAVAILABLE", diagnostic: "Tuổi Thơ chưa kết nối." };
      serviceBanner(result.allowed === true && result.reason === "SERVICE_UNAVAILABLE");
      if (result.allowed === false) block(identity); else unblock();
      const needsOwner = (identity.contentType === "Video" || identity.contentType === "ShortForm" || identity.contentType === "Playable") && !identity.channelHandle && !identity.channelId;
      if (needsOwner && retry++ < 12) retryTimer = setTimeout(evaluate, 500); else retry = 0;
    });
  };
  const schedule = () => { clearTimeout(retryTimer); retryTimer = setTimeout(evaluate, 150); };
  const suppressBlockedInput = event => {
    if (blocked && !event.target.closest?.(`#${overlayId}`)) {
      event.preventDefault();
      event.stopImmediatePropagation();
    }
  };
  document.addEventListener("play", event => { if (blocked && event.target instanceof HTMLMediaElement) event.target.pause(); }, true);
  ["keydown", "keyup", "pointerdown", "mousedown", "touchstart", "click"].forEach(type => document.addEventListener(type, suppressBlockedInput, true));
  new MutationObserver(schedule).observe(document.documentElement, { childList: true, subtree: true });
  addEventListener("yt-navigate-finish", schedule);
  addEventListener("popstate", schedule);
  addEventListener("scroll", schedule, true);
  addEventListener("keydown", event => { if (event.key === "ArrowDown" || event.key === "ArrowUp") schedule(); }, true);
  if (globalThis.__tuoithoYouTubeTestHooks) globalThis.__tuoithoYouTubeTestHooks = { currentShortContainer, currentShortContext, shortsOwnerIdentity, shortsOwnerResolution, content };
  schedule();
})();