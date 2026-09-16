# TASK-005A – Parent Dashboard M3 Foundation

## Phạm vi

Dashboard Parent hoạt động hoàn toàn cục bộ qua named pipe đã xác thực SID và SQLite hiện có. Không thêm cloud, điều khiển từ xa, lọc web/YouTube hoặc thu thập nội dung riêng tư.

## Thời gian sử dụng

- Phụ huynh có thể lưu hạn mức ngày từ 1 đến 1.440 phút.
- Lịch tuần có tối đa hai khung giờ mỗi ngày; giờ kết thúc phải sau giờ bắt đầu và các khung không được chồng lấn.
- Thay đổi quota/lịch không xóa thời gian đã dùng, grant hay quy tắc ứng dụng.
- Toàn bộ mốc lịch dùng múi giờ cục bộ của policy clock và được lưu trong SQLite cùng DeviceTimePolicy.

## Thứ tự quyết định

1. `PARENT_LOCK` luôn ưu tiên cao nhất.
2. Emergency Override đang hoạt động giữ hành vi TASK-003: cho phép tạm thời sau khi Parent Lock đã được bỏ.
3. Nếu không override: ngoài lịch là `OUTSIDE_SCHEDULE`.
4. Nếu trong lịch: hết quota (sau grants) là `QUOTA_EXHAUSTED`.
5. Còn lại là `ALLOWED`.

Grant chỉ tăng quota theo ngày; không vượt qua Parent Lock hoặc lịch sử dụng.

## Hiển thị Parent

Giao diện bình thường sử dụng nhãn tiếng Việt thân thiện và chỉ hiển thị ba tab chính: Tổng quan, Thời gian và Ứng dụng. Tab Chẩn đoán chỉ xuất hiện trong TestMode.