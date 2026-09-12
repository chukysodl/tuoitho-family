# M1 tester package (safe TestMode only)

1. Open PowerShell in the repository and run `./scripts/M1-START.ps1`.
2. The script detects your Windows SID and session, creates a temporary 3-minute M1 policy, forces `TestMode=true`, starts Service and SessionAgent, then opens the Parent CLI.
3. Use the PC for about three minutes. Confirm the child-facing warning and `SIMULATED_LOCK`; Windows remains usable.
4. In Parent CLI choose `1` for +15 minutes. It must display `SUCCESS` and `ALLOWED` status.
5. Restart the Service if desired, then confirm the used time remains recorded.
6. Exit the Parent CLI and run `./scripts/M1-STOP.ps1`.

Do not disable TestMode. The scripts never disconnect, log off, restart, or shut down Windows.