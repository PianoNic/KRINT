using KRINT.Application.Dtos.Activity;
using KRINT.Domain;

namespace KRINT.Application.Mappings.Activity
{
    public static class ActivityEntryMappings
    {
        public static ActivityEntryDto ToDto(this ActivityEntry e) => new()
        {
            Id = e.Id,
            Action = e.Action,
            Target = e.Target,
            InstanceId = e.InstanceId,
            Engine = e.Engine,
            Details = e.Details,
            ActorName = e.ActorName,
            CreatedAt = e.CreatedAt,
        };
    }
}
