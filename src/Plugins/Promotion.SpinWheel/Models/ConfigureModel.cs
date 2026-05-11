namespace Promotion.SpinWheel.Models;

public class ConfigureModel
{
    public bool Enabled { get; set; }
    public int CooldownHours { get; set; } = 72;
    public List<SegmentConfigModel> Segments { get; set; } = new();
}

public class SegmentConfigModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public int ProbabilityWeight { get; set; } = 10;
    public string Color { get; set; } = "#7c3aed";
    public string CouponPrefix { get; set; } = "SPIN";
}
