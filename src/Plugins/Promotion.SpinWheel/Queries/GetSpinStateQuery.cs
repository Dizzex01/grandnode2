using Grand.Business.Core.Interfaces.Common.Configuration;
using Grand.Data;
using MediatR;
using Promotion.SpinWheel.Domain;
using Promotion.SpinWheel.Models;

namespace Promotion.SpinWheel.Queries;

public class GetSpinStateQuery : IRequest<SpinStateResult>
{
    public string CustomerId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
}

public class GetSpinStateQueryHandler(
    IRepository<CustomerSpinRecord> spinRecordRepository,
    ISettingService settingService)
    : IRequestHandler<GetSpinStateQuery, SpinStateResult>
{
    public async Task<SpinStateResult> Handle(GetSpinStateQuery request, CancellationToken cancellationToken)
    {
        var settings = await settingService.LoadSetting<SpinWheelSettings>(request.StoreId);
        var record = await spinRecordRepository.GetOneAsync(r => r.CustomerId == request.CustomerId);

        var now = DateTime.UtcNow;
        var nextSpinAt = record != null
            ? record.LastSpinUtc.AddHours(settings.CooldownHours)
            : (DateTime?)null;
        var canSpin = nextSpinAt == null || nextSpinAt <= now;

        return new SpinStateResult {
            IsEnabled = settings.Enabled,
            CanSpin = canSpin,
            NextSpinAt = canSpin ? null : nextSpinAt,
            Segments = settings.Segments.Select(s => new SegmentDto {
                Id = s.Id,
                Label = s.Label,
                ProbabilityWeight = s.ProbabilityWeight,
                Color = s.Color
            }).ToList()
        };
    }
}
