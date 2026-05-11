namespace Promotion.SpinWheel.Models;

public class SpinStateResult
{
    public bool CanSpin { get; set; }
    public DateTime? NextSpinAt { get; set; }
    public bool IsEnabled { get; set; }
    public List<SegmentDto> Segments { get; set; } = new();
}

public class SegmentDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int ProbabilityWeight { get; set; }
    public string Color { get; set; } = string.Empty;
}
