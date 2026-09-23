# M4 Browser Control (developer-loaded only)

1. Chạy `scripts\M4-BROWSER-INSTALL.cmd`; script tự tạo `browser-control.json`, Extension ID, Chrome/Edge Native Host.
2. Mở `chrome://extensions` hoặc `edge://extensions`, bật **Developer mode**, chọn **Load unpacked**, rồi chọn thư mục được script in ra.
3. Chạy `scripts\M4-BROWSER-CHECK.cmd` để xác nhận cấu hình/registry khớp. Không cần SETX hay sửa JSON.

Extension chỉ gửi provider, host/path, loại nội dung và identity kênh/tài khoản khi có. Nó không gửi hoặc lưu history, nội dung trang, tìm kiếm, bình luận, cookie hay mật khẩu. TikTok FYP/card filtering chưa được thực hiện; M4 chỉ áp dụng chắc chắn cho site, profile và direct video URL.