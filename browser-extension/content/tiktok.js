(() => {
  const provider = "TikTok";
  const overlayId = "tuoitho-tiktok-block";
  let timer = null;
  let lastSignature = "";

  const removeOverlay = () => document.getElementById(overlayId)?.remove();

  const showBlock = (title, detail = null) => {
    removeOverlay();

    const overlay = document.createElement("section");
    overlay.id = overlayId;
    overlay.setAttribute("role", "dialog");
    overlay.setAttribute("aria-modal", "true");

    const card = document.createElement("div");
    const heading = document.createElement("h1");
    heading.textContent = title;
    const platform = document.createElement("p");
    platform.textContent = "TikTok";
    card.append(heading, platform);

    if (detail) {
      const info = document.createElement("p");
      info.textContent = detail;
      card.appendChild(info);
    }

    const back = document.createElement("button");
    back.type = "button";
    back.textContent = "Quay lại";
    back.onclick = () => history.back();
    card.appendChild(back);
    overlay.appendChild(card);

    Object.assign(overlay.style, {
      position: "fixed",
      inset: "0",
      zIndex: "2147483647",
      display: "grid",
      placeItems: "center",
      background: "#101820ee",
      color: "white",
      font: "20px Segoe UI,sans-serif",
      textAlign: "center",
      pointerEvents: "auto"
    });
    Object.assign(card.style, {
      maxWidth: "38rem",
      padding: "2.5rem",
      borderRadius: "1rem",
      background: "#223240"
    });

    document.documentElement.appendChild(overlay);
  };

  const creatorFromPath = () =>
    location.pathname.split("/").find(part => part.startsWith("@")) || null;

  const searchQuery = () => {
    if (!location.pathname.startsWith("/search")) return null;
    try {
      const params = new URL(location.href).searchParams;
      const value =
        params.get("q") ||
        params.get("keyword") ||
        params.get("search_query");
      return value?.normalize("NFC").trim() || null;
    } catch {
      return null;
    }
  };

  const evaluateNavigation = () => {
    const creator = creatorFromPath();
    chrome.runtime.sendMessage({
      type: "tuoitho-navigation",
      payload: {
        profileId: "m1-child",
        managedSessionId: 0,
        provider,
        host: location.hostname,
        path: location.pathname,
        contentType: creator ? "Creator" : "Site",
        tikTokCreator: creator
      }
    }, response => {
      if (response?.allowed === false) {
        showBlock(
          "Nội dung này chưa được phụ huynh cho phép.",
          creator ? `Tài khoản: ${creator}` : null);
      } else {
        removeOverlay();
      }
    });
  };

  const evaluate = () => {
    const signature = location.href;
    if (signature === lastSignature && document.getElementById(overlayId)) return;
    lastSignature = signature;

    const query = searchQuery();
    if (!query) {
      evaluateNavigation();
      return;
    }

    chrome.runtime.sendMessage({
      type: "tuoitho-keyword-check",
      provider,
      query
    }, response => {
      if (response?.allowed === false) {
        showBlock(
          "Từ khóa tìm kiếm này đã bị phụ huynh chặn.",
          `Từ khóa: ${response.matchedKeyword || query}`);
        return;
      }
      evaluateNavigation();
    });
  };

  const schedule = () => {
    clearTimeout(timer);
    timer = setTimeout(evaluate, 120);
  };

  const patchHistory = name => {
    const original = history[name];
    history[name] = function() {
      const result = original.apply(this, arguments);
      schedule();
      return result;
    };
  };

  patchHistory("pushState");
  patchHistory("replaceState");
  addEventListener("popstate", schedule);
  new MutationObserver(schedule).observe(document.documentElement, {
    childList: true,
    subtree: true
  });

  if (globalThis.__tuoithoTikTokTestHooks) {
    globalThis.__tuoithoTikTokTestHooks = { creatorFromPath, searchQuery };
  }

  schedule();
})();
