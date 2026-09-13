# M1 tester package (safe TestMode only)

1. Run `scripts\M1-START.cmd`. It builds Release, detects the current Windows user/session, forces `TestMode=true`, creates a temporary 3-minute policy, sets an M1-only 1-minute idle threshold, starts local components, and opens **Tuổi Thơ – Điều khiển phụ huynh (M1)**.
2. The large **THỜI GIAN CÒN LẠI** display refreshes from the local Service every second. After M1 reset it is exactly `00:03:00`; it decreases only while active and freezes while idle, locked, or unknown.
3. For activity validation, inspect **Nguồn hoạt động** (`RAW_INPUT`), **Nhàn rỗi thiết bị**, and **Nhàn rỗi Windows**. Only device idle is used for accounting. Leave keyboard and mouse untouched for just over one minute: activity becomes `IDLE` and recorded seconds stop increasing. Move the mouse or press a key: device idle resets and activity resumes.
4. The M1-only trace `%TEMP%\tuoitho-m1-activity.csv` is replaced on each start. It contains only timestamp, Raw Input idle seconds, Windows idle seconds, activity state, and recorded active seconds—never key data, coordinates, application data, or screenshots.
5. Use the PC for the 3-minute quota test. Confirm warning and `SIMULATED_LOCK`; Windows must remain usable. Click **+15 phút** and confirm **THÀNH CÔNG** and `ALLOWED` when the schedule permits it.
6. When finished, close the Parent window and run `scripts\M1-STOP.cmd`.

Do not disable TestMode. M1 scripts never disconnect, log off, restart, or shut down Windows. The Parent UI uses secured local named-pipe IPC only; it has no web or cloud control path.