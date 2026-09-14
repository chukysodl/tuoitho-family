# M1 tester package (safe TestMode only)

1. Run `scripts\M1-START.cmd`. It builds Release, detects the current Windows user/session, forces `TestMode=true`, creates a temporary 3-minute policy, sets an M1-only 1-minute idle threshold, starts local components, and opens **Tuổi Thơ – Điều khiển phụ huynh (M1)**.
2. The large **THỜI GIAN CÒN LẠI** display refreshes from the local Service every second. After M1 reset it is exactly `00:03:00`; it decreases only while active and freezes while idle, locked, or unknown.
3. For activity validation, inspect **Nguồn hoạt động** (`RAW_INPUT`), **Nhàn rỗi thiết bị**, and **Nhàn rỗi Windows**. Only device idle is used for accounting. Leave keyboard and mouse untouched for just over one minute: activity becomes `IDLE` and recorded seconds stop increasing. Move the mouse or press a key: device idle resets and activity resumes.
4. The M1-only trace `%TEMP%\tuoitho-m1-activity.csv` is replaced on each start. It contains only timestamp, Raw Input idle seconds, Windows idle seconds, activity state, and recorded active seconds—never key data, coordinates, application data, or screenshots.
5. Use the PC for the 3-minute quota test. At `00:00:00`, a full-screen **HẾT THỜI GIAN SỬ DỤNG** M1 overlay appears; it is visual only and applications remain running. Click **+15 phút** in the Parent UI and confirm the overlay disappears immediately, **THÀNH CÔNG**, and `ALLOWED`.
6. To validate the parent state, click **Khóa bởi phụ huynh**: the same visual overlay appears. Click **Bỏ khóa phụ huynh** and it disappears when quota permits.
7. Only for `m1-child` in TestMode, the overlay includes **Thoát màn hình thử nghiệm**. This hides only the overlay; it does not change policy, quota, grants, or SQLite data.
8. When finished, close the Parent window and run `scripts\M1-STOP.cmd`.

Do not disable TestMode. M1 scripts and the visual overlay never disconnect, log off, restart, shut down Windows, terminate applications, or alter the Windows shell. The Parent UI uses secured local named-pipe IPC only; it has no web or cloud control path.