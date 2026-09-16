(() => {
  const provider = "YouTube";
  const pathInfo = () => location.pathname;
  const channel = () => {
    const handle = document.querySelector('a[href^="/@"]')?.getAttribute('href')?.split('/')[1];
    const id = document.querySelector('link[itemprop="channelId"]')?.getAttribute('content');
    return { channelHandle: handle || null, channelId: id || null };
  };
  const evaluate = () => chrome.runtime.sendMessage({ type: "tuoitho-navigation", payload: { profileId: "m1-child", managedSessionId: 0, provider, host: location.hostname, path: pathInfo(), contentType: location.pathname.startsWith('/shorts/') ? "ShortForm" : location.pathname.startsWith('/watch') ? "Video" : "Channel", ...channel() } }, response => { if (response && response.allowed === false) document.documentElement.innerHTML = '<main style="font:20px sans-serif;padding:3rem"><h1>Nội dung này chưa được phụ huynh cho phép.</h1><p>YouTube</p><button onclick="history.back()">Quay lại</button></main>'; });
  addEventListener('yt-navigate-finish', evaluate); evaluate();
})();