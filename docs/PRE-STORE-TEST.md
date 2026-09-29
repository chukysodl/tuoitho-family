# PRE-STORE TEST – Quản lý thời gian

Mục tiêu: kiểm tra bản production trước khi gắn Store ID thật cho extension Chrome/Edge.

## Chuẩn bị
- Lưu tài liệu đang mở trước khi test khóa.
- Không gỡ bản cũ thủ công trước khi chạy installer; installer sẽ thay Service hiện tại.
- Ghi nhớ mật khẩu phụ huynh đã tạo trong lúc cài.

## Test 1 – Cài đặt
1. Chạy QuanLyThoiGian_Setup.exe bằng quyền Administrator.
2. Thiết lập mật khẩu phụ huynh khi được yêu cầu.
3. Cài đặt chỉ được hoàn tất khi kiểm tra bảo vệ PASS.
4. Mở Quản lý thời gian từ Desktop/Start Menu.

PASS:
- Không yêu cầu cài .NET hay runtime khác.
- Service chạy.
- Giao diện mở bình thường.
- Không có cửa sổ console SessionAgent.

## Test 2 – Mật khẩu phụ huynh
1. Bấm +15 phút hoặc Khóa/Bỏ khóa.
2. Phải hỏi mật khẩu phụ huynh.
3. Nhập sai → không thay đổi.
4. Nhập đúng → thực hiện được.
5. Trong vòng 5 phút có thể thao tác tiếp mà không hỏi lại.

## Test 3 – Task Manager
1. Mở Task Manager.
2. End Task TuoiTho.SessionAgent.exe.
3. Chờ tối đa khoảng 2–4 giây.
4. Kiểm tra SessionAgent xuất hiện lại.
5. Nếu đang khóa, màn hình khóa phải hiện lại.

PASS:
- End Task không biến thành cách tắt bảo vệ.

## Test 4 – Đóng Parent UI
1. Đóng cửa sổ Quản lý thời gian.
2. Chờ 10 giây.
3. Kiểm tra hạn mức/khóa vẫn hoạt động.

PASS:
- Parent UI chỉ là bảng điều khiển; đóng UI không dừng bảo vệ.

## Test 5 – Khóa rồi restart
1. Từ điện thoại hoặc Parent UI bấm Khóa ngay.
2. Xác nhận màn hình khóa Quản lý thời gian hiện.
3. Restart Windows.
4. Đăng nhập lại.

PASS:
- Service tự chạy.
- SessionAgent tự chạy lại.
- Nếu ParentLock vẫn còn thì màn hình khóa phải quay lại mà không cần mở Parent UI.

## Test 6 – End Task khi đang khóa
1. Khi màn hình khóa đang hiện, thử Alt+F4.
2. Thử Alt+Tab.
3. Ctrl+Alt+Del → mở Task Manager → End Task SessionAgent.
4. Quay lại Desktop.

PASS:
- Alt+F4 không thoát khóa.
- Nếu SessionAgent bị kill, watchdog dựng lại và khóa trở lại.

Lưu ý: Ctrl+Alt+Del là màn hình bảo mật của Windows; ứng dụng không thể chặn bản thân tổ hợp này. Mục tiêu là sau khi quay lại Desktop, bảo vệ vẫn tồn tại.

## Test 7 – Service Recovery
1. Trong Task Manager > Details, kết thúc TuoiTho.Service.exe nếu Windows cho phép.
2. Chờ khoảng 2–5 giây.
3. Kiểm tra Service chạy lại.

PASS:
- Service Recovery của Windows khởi động lại process.

## Test 8 – Gỡ cài đặt
1. Settings > Apps > Installed apps > Quản lý thời gian > Uninstall.
2. Nhập sai mật khẩu → phải từ chối.
3. Nhập đúng mật khẩu → cho gỡ với UAC Administrator.

## Kiểm tra nhanh
Start Menu > Quản lý thời gian > Kiểm tra bảo vệ

Kết quả mong muốn:
PRODUCTION CHECK: PASS

## Browser extension
Bản Pre-Store Test KHÔNG ghi ExtensionInstallForcelist giả.
- Nếu máy đã có extension test từ trước, cấu hình cũ được giữ.
- Máy cài sạch chưa tự force-install extension cho tới khi có Chrome Web Store / Edge Add-ons ID thật.
- Đây là phần cuối cùng trước installer production chính thức.
