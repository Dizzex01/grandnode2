using Grand.Domain.Configuration;

namespace Promotion.SpinWheel.Domain;

public class SpinWheelSettings : ISettings
{
    public bool Enabled { get; set; }
    public int CooldownHours { get; set; } = 72;
    public List<SpinSegment> Segments { get; set; } = new();
}

public class SpinSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public int ProbabilityWeight { get; set; } = 10;
    public string Color { get; set; } = "#7c3aed";
    public string CouponPrefix { get; set; } = "SPIN";
}
