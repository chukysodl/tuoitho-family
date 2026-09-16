# M4 Browser Control (developer-loaded only)

1. Chạy `scripts\M4-BROWSER-INSTALL.cmd`.
2. Mở `chrome://extensions` hoặc `edge://extensions`, bật **Developer mode**, chọn **Load unpacked**, rồi chọn thư mục được script in ra.
3. Sao chép Extension ID, đặt biến môi trường `TUOITHO_EXTENSION_ID`, rồi chạy lại install script để Native Messaging Host chỉ chấp nhận extension đó.

Extension chỉ gửi provider, host/path, loại nội dung và identity kênh/tài khoản khi có. Nó không gửi hoặc lưu history, nội dung trang, tìm kiếm, bình luận, cookie hay mật khẩu. TikTok FYP/card filtering chưa được thực hiện; M4 chỉ áp dụng chắc chắn cho site, profile và direct video URL.