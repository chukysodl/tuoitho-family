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
  const shortsOwnerIdentity = () => {
    const active = document.querySelector("ytd-reel-video-renderer[is-active], ytd-reel-video-renderer[is-active='true']");
    if (!active) return {};
    const link = active.querySelector("ytd-reel-player-header-renderer a[href], #channel-name a[href], ytd-channel-name a[href]");
    const identity = identityFromHref(link?.getAttribute("href"));
    return { ...identity, channelId: channelIdFrom(active) || identity.channelId || null };
  };
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
    if (path.startsWith("/shorts/")) return { contentType: "ShortForm", ...shortsOwnerIdentity() };
    if (path.startsWith("/playables/")) return { contentType: "Playable", ...playableOwnerIdentity() };
    if (path === "/watch") return { contentType: "Video", ...ownerIdentity() };
    return { contentType: "Site" };
  };
  const evaluate = () => {
    clearTimeout(retryTimer);
    const identity = content();
    chrome.runtime.sendMessage({ type: "tuoitho-navigation", payload: { profileId: "m1-child", managedSessionId: 0, provider, host: location.hostname, path: location.pathname, ...identity } }, response => {
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
  schedule();
})();
