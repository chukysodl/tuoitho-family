const HOST = "com.tuoitho.browserhost";
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (!message || message.type !== "tuoitho-navigation" || !sender.tab) return;
  chrome.runtime.sendNativeMessage(HOST, { ...message.payload, extensionId: chrome.runtime.id }, result => {
    if (chrome.runtime.lastError) return reply({ allowed: true, reason: "SERVICE_UNAVAILABLE", diagnostic: "Tuổi Thơ chưa kết nối." });
    reply(result || { allowed: true, reason: "SERVICE_UNAVAILABLE", diagnostic: "Tuổi Thơ chưa kết nối." });
  });
  return true;
});
