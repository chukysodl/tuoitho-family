# M1 tester package (safe TestMode only)

1. Open the repository folder and run `scripts\M1-START.cmd`. The CMD launcher automatically starts PowerShell with a Process-only `ExecutionPolicy Bypass`, so you do not need to change the machine/user PowerShell policy manually. It builds the Release app, detects your current Windows user/session, forces `TestMode=true`, creates a temporary 3-minute policy, starts the local components, and opens **Tuổi Thơ – Điều khiển phụ huynh (M1)**.
2. In the Parent window click **Làm mới trạng thái**. Confirm the profile, Windows session, `BẬT (không khóa thật)`, 3-minute quota, remaining time, and state are shown.
3. Use the PC for about three minutes. Confirm the child warning and `SIMULATED_LOCK`; Windows must remain usable.
4. Click **+15 phút**, then **Làm mới trạng thái**. The result must say **THÀNH CÔNG** and state must become `ALLOWED` when the schedule permits it.
5. To try parent controls, use the clearly labelled Override and Parent Lock buttons. Each action reports **THÀNH CÔNG** or **TỪ CHỐI** from the secured local Service response.
6. When finished, close the Parent window and run `scripts\M1-STOP.cmd`.

Do not disable TestMode. The `.cmd` launchers bypass script execution policy only for the temporary PowerShell process they start; they do not change the permanent Windows/PowerShell policy. M1 scripts never disconnect, log off, restart, or shut down Windows. The Parent window uses only the secured local named-pipe IPC; it has no web or cloud control path.
