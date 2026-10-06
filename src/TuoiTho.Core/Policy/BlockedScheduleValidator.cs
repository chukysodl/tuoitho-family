namespace TuoiTho.Core.Policy;

/// <summary>Validates daily repeating blocked windows, including overnight ranges.</summary>
public static class BlockedScheduleValidator
{
    public const int MaximumWindows = 6;

    public static string? Validate(IReadOnlyList<BlockedUsageWindow>? windows)
    {
        if (windows is null) return "Khung giờ cấm không hợp lệ.";
        if (windows.Count > MaximumWindows) return $"Chỉ được có tối đa {MaximumWindows} khung giờ cấm.";

        var segments = new List<(int Start, int End)>();
        foreach (var window in windows)
        {
            if (window.Start == window.End)
                return "Khung giờ cấm phải có giờ bắt đầu và kết thúc khác nhau.";

            var start = window.Start.Hour * 60 + window.Start.Minute;
            var end = window.End.Hour * 60 + window.End.Minute;

            if (start < end)
            {
                segments.Add((start, end));
            }
            else
            {
                segments.Add((start, 24 * 60));
                if (end > 0) segments.Add((0, end));
            }
        }

        var ordered = segments.OrderBy(segment => segment.Start).ThenBy(segment => segment.End).ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index].Start < ordered[index - 1].End)
                return "Các khung giờ cấm đang bị chồng lấn.";
        }

        return null;
    }
}
