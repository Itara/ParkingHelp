using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;

namespace ParkingHelp.DB.QueryCondition
{
    public class HelpHistoryGetParam
    {
        [SwaggerSchema("조회할 멤버 고유 ID", Format = "string")]
        public string? HelperMemberId { get; set; }
        [SwaggerSchema("조회 시작 날짜", Format = "date-time")]
        public DateTimeOffset? FromHelpDate { get; set; } 
        [SwaggerSchema("조회 종료 날짜", Format = "date-time")]
        public DateTimeOffset? ToHelpDate { get; set; }
    }
}
