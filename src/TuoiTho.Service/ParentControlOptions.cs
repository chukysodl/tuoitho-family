namespace TuoiTho.Service;

public sealed class ParentControlOptions
{
    public const string SectionName = "ParentControl";
    public string PipeName { get; init; } = "TuoiTho.ParentControl";
    public string[] AllowedParentSids { get; init; } = [];
}