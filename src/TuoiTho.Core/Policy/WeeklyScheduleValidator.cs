namespace TuoiTho.Core.Policy;

/// <summary>Validates the parent-editable weekly time windows without rewriting their input.</summary>
public static class WeeklyScheduleValidator
{
    public const int MaximumWindowsPerDay = 2;

    public static string? Validate(IReadOnlyList<AllowedUsageWindow>? windows)
    {
        if (windows is null) return "Khung giờ sử dụng không hợp lệ.";
        foreach (var group in windows.GroupBy(window => window.Day))
        {
            var day = group.OrderBy(window => window.Start).ToArray();
            if (day.Length > MaximumWindowsPerDay) return $"{VietnameseDay(group.Key)} chỉ được có tối đa {MaximumWindowsPerDay} khung giờ.";
            foreach (var window in day)
                if (window.End <= window.Start) return $"Giờ kết thúc phải sau giờ bắt đầu ({VietnameseDay(window.Day)}).";
            for (var index = 1; index < day.Length; index++)
                if (day[index].Start < day[index - 1].End) return $"Các khung giờ {VietnameseDay(group.Key)} đang bị chồng lấn.";
        }
        return null;
    }

    public static string VietnameseDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Thứ Hai", DayOfWeek.Tuesday => "Thứ Ba", DayOfWeek.Wednesday => "Thứ Tư",
        DayOfWeek.Thursday => "Thứ Năm", DayOfWeek.Friday => "Thứ Sáu", DayOfWeek.Saturday => "Thứ Bảy",
        _ => "Chủ Nhật"
    };
}

/// <summary>Friendly text for ordinary Parent UI; diagnostics may still show technical values.</summary>
public static class ParentPolicyStateText
{
    public static string ToVietnamese(string? state) => state switch
    {
        "ALLOWED" => "Được phép sử dụng",
        "OVERRIDE" => "Đang được phụ huynh cho phép khẩn cấp",
        "PARENT_LOCK" => "Đã khóa bởi phụ huynh",
        "QUOTA_EXHAUSTED" => "Đã hết thời gian hôm nay",
        "OUTSIDE_SCHEDULE" => "Ngoài khung giờ được phép",
        _ => "Chưa xác định"
    };
}