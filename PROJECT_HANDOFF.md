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