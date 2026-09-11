# Tuổi Thơ – Family Device Manager

Phần mềm miễn phí/mã nguồn mở giúp phụ huynh **quản lý** việc sử dụng máy tính của trẻ, không phải phần mềm giám sát.

## Nguyên tắc lõi

1. Phụ huynh quyết định **khi nào** trẻ được dùng máy.
2. Phụ huynh quyết định **ứng dụng/game nào** được phép hoặc bị chặn.
3. Phụ huynh quyết định **website nào** được phép hoặc bị chặn.
4. Có thể đặt giới hạn thời gian riêng cho từng ứng dụng/game.
5. YouTube có module riêng: cho/chặn kênh và danh sách từ khóa tìm kiếm được phép.
6. Không keylogger, không chụp màn hình bí mật, không đọc tin nhắn/mật khẩu.
7. Local-first: chức năng lõi vẫn hoạt động khi mất Internet.
8. Điều khiển từ xa là module tùy chọn và phải có phương án miễn phí.

## Công nghệ định hướng

- Windows Agent/Service: C# / .NET 10 LTS
- Local database: SQLite
- Parent UI: ASP.NET Core + responsive web/PWA hoặc desktop shell nhẹ
- Browser control: Chrome/Edge Extension Manifest V3 + native/local bridge
- Remote: adapter tách rời; triển khai đầu tiên dùng hạ tầng free-tier, không khóa nhà cung cấp

## Cấu trúc solution

- `src/TuoiTho.Core`: domain thuần .NET, không phụ thuộc Windows, UI hoặc cloud.
- `src/TuoiTho.Storage`: SQLite local-first và schema migrations.
- `src/TuoiTho.Service`: Windows Service host cho enforcement.
- `src/TuoiTho.SessionAgent`: host trong child session cho countdown/warnings.
- `src/TuoiTho.Parent`: ASP.NET Core host cho parent dashboard.
- `tests/TuoiTho.Tests`: unit, SQLite integration và dependency sanity tests.

## Phát triển local

Yêu cầu .NET SDK 10.x. Từ thư mục gốc repository:

```powershell
./scripts/build.ps1
./scripts/system-check.ps1
```

Các lệnh tương đương là `dotnet restore TuoiTho.sln`, `dotnet build TuoiTho.sln --configuration Release` và `dotnet test TuoiTho.sln --configuration Release`. SQLite được tạo tại đường dẫn do host cấu hình; file database local không được commit.

Warnings được coi là lỗi trong mọi project. Nullable reference types và .NET analyzers được bật tập trung trong `Directory.Build.props`. Log runtime dùng JSON structured logging và chỉ ghi component/event vận hành, không ghi nội dung cá nhân.

Danh sách package cùng license được ghi trong `docs/DEPENDENCIES.md`; hướng dẫn chi tiết nằm trong `docs/DEVELOPMENT.md`.

## Bắt đầu cho Codex

Đọc theo thứ tự:

1. `PROJECT_HANDOFF.md`
2. `docs/PRODUCT_REQUIREMENTS.md`
3. `docs/ARCHITECTURE.md`
4. `docs/CODEX_RULES.md`
5. `TASK_INDEX.md`
6. Task đang được giao trong thư mục `tasks/`

Không làm nhiều task một lúc nếu task trước chưa PASS.
