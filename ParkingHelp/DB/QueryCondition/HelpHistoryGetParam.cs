using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;

namespace ParkingHelp.DB.QueryCondition
{
    public class HelpHistoryGetParam
    {
        [SwaggerSchema("도움을 준 Member ID", Format = "int")]
        public int HelperMemId { get; set; }
        [SwaggerSchema("조회 시작 날짜", Format = "date-time")]
        public DateTimeOffset? FromHelpDate { get; set; } 
        [SwaggerSchema("조회 종료 날짜", Format = "date-time")]
        public DateTimeOffset? ToHelpDate { get; set; }
    }
}
