import { TEST_MODE } from "./m4-runtime-config.js";

const HOST = "com.tuoitho.browserhost";
const unavailable = diagnostic => ({
  allowed: TEST_MODE,
  reason: "SERVICE_UNAVAILABLE",
  diagnostic: diagnostic || "Tuổi Thơ chưa kết nối."
});

chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (!message || message.type !== "tuoitho-navigation" || !sender.tab) return;
  chrome.runtime.sendNativeMessage(HOST, { ...message.payload, extensionId: chrome.runtime.id }, result => {
    if (chrome.runtime.lastError) {
      const diagnostic = chrome.runtime.lastError.message || "Native Messaging không khả dụng.";
      // Runtime-only extension diagnostic: it is neither persisted nor sent from page input.
      console.warn("[TuoiTho M4] Native Messaging:", diagnostic);
      reply(unavailable(diagnostic));
      return;
    }
    reply(result || unavailable("BrowserHost không phản hồi."));
  });
  return true;
});
