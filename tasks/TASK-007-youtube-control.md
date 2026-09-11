# TASK-007 – YouTube channel/search controls

## Goal
Allow YouTube while giving parent granular control over channels/search.

## Implement
YouTube module in browser extension with policy options:
- YouTube Allowed/Blocked;
- Channel mode: Allowlist / Blocklist;
- channel rule should prefer stable channel ID; retain friendly title only for display;
- Search mode: unrestricted / keyword allowlist / search disabled;
- keyword normalization defined (case, accents, whitespace);
- optional Block Shorts;
- clear blocked page/message and Request Access;
- handle YouTube SPA navigation, direct video links and search URLs;
- avoid mandatory YouTube Data API key/quota for core enforcement if possible;
- never treat arbitrary page text as authorization.

## Tests
Fixtures/unit tests for URL parsing, channel decisions, keyword matching, Shorts decision and SPA navigation events.

## User acceptance M4
Parent allows selected educational/chess channels and allowed search words; unapproved channel/search is blocked; normal allowed viewing works.

## PASS
Tests/build + real Chrome/Edge system check PASS.
Commit + push `task/007-youtube-control`.
