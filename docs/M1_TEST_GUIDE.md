# M1 TestMode-only guide

1. Keep `TestMode: true`. Do **not** disable it for M1.
2. Set a 3–5 minute quota and a schedule covering the current time for the managed child profile/session.
3. Start TuoiTho.Service and TuoiTho.SessionAgent in that child session; verify the child receives warning/status messages.
4. When quota expires, verify `SIMULATED_LOCK` in Service logs. The Windows session must remain usable.
5. In the local Parent harness, send `POST /m1/{profileId}/{sessionId}/GrantMinutes?minutes=15` (or 30/60/custom). Verify the status becomes allowed.
6. Restart the Service, then verify used time remains and the quota has not reset. Rebooting must not grant extra active time.
7. For recovery, keep TestMode enabled and use EmergencyOverride or a +15 grant through the Parent harness. Never test on an administrator, parent, or unrelated session.