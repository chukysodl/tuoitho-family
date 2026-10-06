# PROJECT HANDOFF – TUỔI THƠ

## 1. Mục tiêu

Xây dựng phần mềm quản lý thiết bị cho gia đình, ưu tiên Windows trước. Mục tiêu là giúp trẻ có thời gian sử dụng thiết bị hợp lý và chỉ tiếp cận nội dung/phần mềm mà phụ huynh đã lựa chọn.

## 2. Không phải phần mềm theo dõi

Tuyệt đối không thêm:
- keylogger;
- chụp màn hình định kỳ/bí mật;
- đọc email/tin nhắn/mật khẩu;
- ghi âm/camera bí mật;
- thu thập nội dung cá nhân không cần thiết.

Chỉ lưu dữ liệu cần cho quản lý và vận hành chính sách.

## 3. Mô hình chính sách

### Thời gian
- tổng số phút/ngày;
- khung giờ được dùng;
- lịch theo ngày trong tuần;
- thêm thời gian 15/30/60 phút;
- hết giờ -> khóa phiên sử dụng;
- cảnh báo trước khi hết giờ.

### Ứng dụng/game
Mỗi ứng dụng có thể:
- Allow;
- Block;
- Allow theo khung giờ;
- Allow tối đa X phút/ngày.

Không phân biệt game tốt/xấu theo loại. Phụ huynh quyết định từng ứng dụng. Ví dụ: cờ vua Allow, cờ tướng Allow, Minecraft Block.

### Website
Hỗ trợ 2 chế độ:
- Allowlist strict: website chưa được duyệt -> chặn;
- Mixed: allow/block theo rule.

Bản đầu ưu tiên Allowlist strict.

### YouTube
Module riêng, mục tiêu:
- allow/block channel;
- allowlist từ khóa tìm kiếm;
- tùy chọn chặn Shorts;
- tùy chọn giới hạn YouTube theo thời gian.

Không dùng lịch sử xem để giám sát trẻ nếu không cần cho enforcement.

## 4. Local-first

Máy trẻ phải thực thi chính sách ngay cả khi Internet mất. Database chính sách và bộ đếm thời gian nằm cục bộ.

Remote control chỉ là kênh đồng bộ/lệnh:
- LOCK NOW;
- UNLOCK / GRANT TIME;
- thay đổi policy;
- duyệt yêu cầu ứng dụng/site.

Nếu cloud chết, policy hiện tại vẫn tiếp tục hoạt động.

## 5. Định hướng miễn phí cho cộng đồng

- ưu tiên dependency miễn phí/mã nguồn mở;
- không yêu cầu subscription để dùng chức năng lõi;
- remote backend phải có adapter để thay đổi/self-host;
- không phụ thuộc một vendor đến mức dự án chết nếu free tier thay đổi.

## 6. Triết lý nghiệm thu

Ưu tiên độ chắc chắn hơn số lượng tính năng. Mỗi task phải build + test + tự sửa lỗi trước khi commit/push.

User chỉ nên phải test các mốc lớn, không test các bước kỹ thuật nhỏ.

## 7. Trạng thái bàn giao

- TASK-003 hoàn tất và đã được M1 chấp nhận tại commit `8d2cad5`.
- Kiểm thử M1 luôn giữ `TestMode=true`; lớp phủ hết giờ chỉ là mô phỏng an toàn, không khóa, đăng xuất, tắt máy hoặc chấm dứt ứng dụng Windows.
- GitHub Actions run `34815667678` đã PASS.

- TASK-004A hoàn tất và được M2 chấp nhận trên máy thật tại commit `f94c7b1`; chính sách ứng dụng vẫn chỉ mô phỏng trên nhánh `task/004-app-control` cho đến khi M2 explicit-block được kiểm thử an toàn.
- TASK-004B1 PASS — real user acceptance: TestMode luôn bật, công tắc thực thi mặc định tắt sau khởi động, và chỉ PID Calculator đã được định danh/ràng buộc session mới được đóng trong thử nghiệm M2; tất cả lớp hệ thống, nền, control-plane và ứng dụng chưa duyệt đều fail-closed.
- TASK-004B2 PASS — real M2 TestMode proof: allowlist-first/deny-by-default có preflight scan, bulk baseline, lease 10 phút không lưu bền, và luôn tắt sau Service restart.
- TASK-004 PASS / COMPLETE trên `main` tại `f277036eaa748e58f33f0ff92c419ca0a8e2be27`: final closeout deduplicates case-insensitive executable identities (newest observation/rule state preserved). Branch Actions `35071280376` PASS; Main Actions `35071468605` PASS.

- M1 soft-lock recovery is deliberately visual-only: the same-screen recovery panel foregrounds the local Parent UI or hides the overlay with F12 without changing policy, quota, grants, TestMode, or SQLite state.
- TASK-005A bổ sung Parent Dashboard local-first: editor quota ngày và lịch tuần (tối đa hai khung/ngày), kiểm tra tiếng Việt, SQLite persistence và tab Thời gian. Precedence: Parent Lock > Emergency Override tương thích TASK-003 > Schedule > Quota > Allowed; grant chỉ tăng quota, không vượt lock/lịch. Commit `cafab97f95c003825e8ca6a5a0190bf8651d3e0c`; Actions `35086492938` PASS.

## 8. Verified closeout — 2026-09-23

- `main` includes TASK-005 and TASK-006. Integrated baseline: `cf5d3a7954690a6af689ea78adfb1f2c1b4a3dd7`; GitHub Actions run `35803055541` PASS.
- TASK-005 Parent Dashboard, daily quota, and weekly schedule are complete and user-confirmed (M3).
- TASK-006 custom website policy, Chrome/Edge Native Messaging, YouTube channel/Shorts/Playables, TikTok creator controls, and stable extension update/repair flow are complete and user-confirmed (M4).
- TASK-007 is partial: channel/Shorts work shipped with TASK-006; YouTube search-keyword controls are not present in the current code.
- TASK-008 depends on TASK-005 and can proceed independently of the remaining TASK-007 search work.
- Preserve the local uncommitted `Directory.Packages.props` change in the original developer worktree; it is not part of the verified baseline.

## 9. TASK-008 remote control implementation

- `task/008-remote-control` adds Core provider-neutral remote contracts, SQLite replay/policy persistence, DPAPI-protected device identity, a Supabase Edge Function adapter, a mobile-first authenticated dashboard, and Parent UI pairing-code generation.
- Implementation commit `912222cae57c6d2a1578c8e3e3b314d1b3397cfb` passed GitHub Actions run `35806837895` (including Deno Edge Function type checking).
- Remote lock/unlock/grant reuse the local policy actions; unlock clears Parent Lock only, and schedule/quota remain authoritative. SyncPolicy is validated and applied atomically while preserving local TestMode, managed SID/session, Parent Lock, and Emergency Override.
- Cloud outages are best-effort transport failures only; the local SQLite policy/enforcement path does not depend on network availability.
- Automated coverage uses a fake transport plus SQLite persistence and dashboard/Edge Function security fixtures. No Supabase project credentials were available, so no cloud deployment or external-network phone acceptance has been performed.
- M5 requires configuring/deploying a Supabase project and completing the short external-network acceptance in `docs/TASK_008_REMOTE_CONTROL.md`. Keep TestMode enabled during the first remote lock/unlock acceptance.
- TASK-008B adds a protected machine-wide public Supabase config, one-step CLI deployment scripts, an explicit database health probe, a GitHub Pages workflow for `remote-dashboard`, and a secret-safe runtime preflight. It does not embed API keys in the dashboard or set/disable TestMode.
- `M5-TEST-GUIDE.txt` is the parent-facing acceptance checklist. Real provider deployment, `M5 REMOTE CHECK: READY`, and phone acceptance remain pending until a parent logs into/creates the Supabase project, supplies the Project Ref and public key, and tests over mobile data. Do not merge TASK-008 into `main` before those checks pass.


## 10. TASK-009 Tamper Protection v2 — 2026-09-30

- Branch: `task/009-installer-hardening`.
- Verified code head: `969ec5cb0bf6160f547d07fb8bd2a74f0644d607`.
- GitHub Actions: Build and test run `36718235743` PASS; production installer run `36718235745` PASS.
- Root cause fixed: the previous parent password guarded only the official Inno Setup uninstall entry point, so a third-party uninstaller could bypass that UI flow.
- The Windows Service now owns the tamper-protection state. Normal mode applies a hardened service DACL and protected install-file ACLs. LocalSystem retains full control; Administrators keep read/start access but do not receive ordinary stop/delete/change-config/full file-write access while protection is locked.
- AdminTool no longer treats the password as a UI-only gate. After the current parent password is verified, it requests a short maintenance window from the running Service over a local named pipe. The Service independently verifies the stored PBKDF2 parent password before temporarily restoring Administrator maintenance rights.
- The maintenance window is bounded (default 180 seconds; hard limits 30–600 seconds). After expiry the Service re-applies hardened state.
- Repair/upgrade and official uninstall use the same maintenance authorization flow. The production check now validates that the hardened service DACL is actually present.
- Added automated tests for hardened/maintenance service ACL contracts and maintenance-window bounds.
- Browser force-install policy remains machine-wide where real Store extension IDs are supplied.
- Acceptance gate: on a real Windows machine, reboot, confirm protection is active, then test (1) Task Manager/Services stop attempts, (2) IObit uninstall without the parent password, (3) official uninstall with the parent password, and (4) reboot persistence.
- Security boundary: this is strong user-mode hardening. It does not claim to defeat a determined actor already executing arbitrary code as LocalSystem/kernel/PPL. If a third-party remover succeeds through a SYSTEM helper, do not add an unsigned/homebrew kernel driver; use a Standard child account plus Windows policy/app-control controls as the next defensive layer.


## 11. TASK-009E Account Protection Mode — 2026-10-06

- Real IObit testing proved that TASK-009 user-mode DACL/service hardening does not prevent an already elevated third-party uninstaller from forcibly removing registry entries, files, and the Windows Service. Do not describe the parent-password uninstall hook as absolute tamper protection.
- Version 1.0.4 changes the security model instead of adding another cosmetic password gate.
- The Parent UI now has a TÀI KHOẢN tab that inventories local Windows users, Administrator membership, UAC status, and the configured managed-child SID.
- A parent may convert a selected account to the child role. Safety rules prevent demoting the currently logged-in parent account, the built-in Administrator, or the last remaining enabled Administrator. The operation requires both the local parent authorization gate and Windows UAC elevation.
- Account selection is stored machine-wide at ProgramData/TuoiTho/account-protection.json. The Service reads it dynamically and only launches/rebinds SessionAgent for the configured Standard User.
- Administrator and unrelated sessions are explicitly skipped. A stale SessionAgent left in a parent/non-child session is terminated only after exact executable-path and session matching.
- When no managed-child SID has been configured yet, Administrator sessions are still excluded from child enforcement; the Parent UI reports Account Protection as UNSAFE until an actual Standard child account is selected and UAC is enabled.
- Intended acceptance: install/upgrade 1.0.4, open TÀI KHOẢN, select the child's Windows account, press Đặt làm tài khoản trẻ, sign into that child account, then try IObit/Revo/Services/Registry. Those tools may launch, but elevation/destructive admin actions must require parent Administrator credentials. Do not test the anti-uninstall claim from the parent Administrator account because Windows Administrator remains the trusted security boundary.
- Release tag: account-protection-test-20261006.
