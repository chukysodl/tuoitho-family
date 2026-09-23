# TASK-006B — Custom website / URL blocking

TuoiTho uses Manifest V3 `declarativeNetRequest` dynamic rules for parent-created generic website policy. SQLite and the local Service are the source of truth; the extension stores only the most recent policy snapshot/revision and its TuoiTho-owned dynamic rule IDs.

## Rules and precedence

A parent may create `DOMAIN` or `PATH_PREFIX` rules with `BLOCK` or `ALLOW`. Hosts are normalized to lowercase punycode internally while the UI retains a friendly Unicode display. A domain covers that host and its subdomains only. A path prefix is host-bound. The matching rule with the longest/more-specific path wins; this makes `BLOCK example.com` plus `ALLOW example.com/learning/` deterministic.

## Privacy and safety boundary

Only parent-authored rules are persisted. Normal arbitrary navigation never sends a URL to the Service: browser startup/top-level navigation asks only for the policy revision/snapshot, then DNR evaluates locally. The extension has no arbitrary-site content script and does not collect page content, browsing/watch/search history, cookies, passwords, keystrokes, screenshots, documents, or query strings.

TuoiTho rules apply only to `main_frame` and `sub_frame`, so a blocked domain is not used to block unrelated third-party assets. Existing block DNR rules remain while BrowserHost/Service is unavailable. Internal browser and extension URLs are rejected from parent input. Browser dynamic rules not owned by TuoiTho are never removed or modified.
## DNR redirect verification (006B1)

`blocked.html` is the only extension resource declared web-accessible, solely so a dynamic DNR redirect can render the TuoiTho block page. In TestMode the extension reports memory-only diagnostics through the existing secured local pipe: `Custom rules in policy`, `DNR rules active`, `DNR_SYNC` and a bounded, URL-free `DNR_ERROR`. A failed `updateDynamicRules` call leaves the prior DNR snapshot in place and is not persisted as a bypass.
## Stable M4 extension identity (006B2)

The staged extension folder (`%LOCALAPPDATA%\TuoiTho\M4\Extension`) owns a Chromium public key and its derived extension ID. The repository manifest deliberately has **no** `key`; never copy that raw manifest over the staged folder. Use `M4-BROWSER-UPDATE.cmd` for code updates: it preserves the existing staged key, verifies the ID against `%ProgramData%\TuoiTho\browser-control.json`, and refreshes the single allowed Chrome/Edge Native Messaging origin.

`M4-BROWSER-INSTALL.cmd` is idempotent: first install generates one identity; later runs retain it. If an old staged key has genuinely been lost, use `M4-BROWSER-REPAIR.cmd`. Repair creates one canonical replacement only when needed, preserves profile/session/SID/TestMode configuration and all SQLite policies, and asks the parent to remove old TuoiTho unpacked entries manually before loading only the staged folder. It never removes arbitrary browser extensions.