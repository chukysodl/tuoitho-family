# Product Requirements

## P0 – bắt buộc cho MVP Windows

### A. Quản lý thời gian
- Agent tự khởi động cùng Windows.
- Ghi nhận thời gian sử dụng theo ngày.
- Phân biệt máy đang active với idle/locked đủ để không tính sai thời gian.
- Daily quota.
- Allowed time windows.
- Warning trước khi hết giờ.
- Hết quyền sử dụng -> khóa child session.
- Parent PIN để override tại máy.

### B. Quản lý ứng dụng
- Liệt kê app/process đã cài hoặc phát hiện để phụ huynh chọn.
- Rule theo executable identity, không chỉ tên hiển thị.
- Allow / Block.
- Schedule per app.
- Daily quota per app.
- App chưa được duyệt: xử lý theo DefaultAppPolicy.
- MVP mặc định hướng tới `BlockUnknown`, nhưng UI phải cho phụ huynh đổi policy.

### C. Quản lý web
- Chỉ cho phép browser đã được quản lý.
- DefaultWebPolicy hỗ trợ `AllowlistOnly`.
- Domain/subdomain rules.
- Trang block thân thiện, cho phép trẻ gửi Request Access.
- Trình duyệt khác chưa được quản lý phải có thể bị chặn bằng App Policy.

### D. YouTube
- Allow/block channel theo stable identifier khi có thể.
- Search keyword allowlist.
- Tùy chọn block Shorts.
- Rule YouTube nằm riêng với generic web rule.

### E. Parent Dashboard
- Xem trạng thái máy.
- Thời gian còn lại hôm nay.
- Sửa lịch/quota.
- Sửa app rules.
- Sửa web rules.
- Sửa YouTube rules.
- Lock now.
- Grant time.
- Xem request chờ duyệt.

### F. Remote
- Pair device an toàn.
- Lệnh remote được xác thực và chống replay.
- Nếu offline thì queue/sync hợp lý.
- Không được khiến local enforcement phụ thuộc cloud.

## P1 – sau MVP
- nhiều trẻ/nhiều profile;
- nhiều máy cho một trẻ;
- thống kê tuần;
- rule template theo tuổi;
- import/export policy;
- Android feasibility/prototype.

## Out of scope
- keylogging;
- screenshot surveillance;
- chat/message interception;
- camera/mic monitoring;
- phá/bypass bảo mật hệ điều hành;
- ẩn agent khỏi phụ huynh/người quản trị máy.
