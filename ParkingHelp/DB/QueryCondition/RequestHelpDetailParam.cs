using ParkingHelp.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ParkingHelp.DB.QueryCondition
{
    public class RequestHelpDetailPutParam
    {
        public DateTimeOffset? DisCountApplyDate { get; set; }
        public DiscountApplyType? DisCountApplyType { get; set; }
        public ReqDetailStatus? ReqDetailStatus { get; set; }
        public int? HelperMemId { get; set; }
        public int? DisApplyCount { get; set; }
    }

    public class RequestHelpDetailMultiUpdatePutParam
    {
        [SwaggerSchema("일괄 수정할 요청 고유 ID", Format = "int")]
        public int ReqId { get; set; }
        [SwaggerSchema("현재 요청 수락 갯수", Format = "int")]
        public int? DiscountApplyCount { get; set; }
        [SwaggerSchema("요청 상태", Format = "int")]
        public HelpStatus?  HelpStatus { get; set; }
        [SwaggerSchema("일괄 수정할 요청세부의 상태값 ", Format = "int")]
        public ReqDetailStatus UpdateTargetReqDetailStatus { get; set; }
        [SwaggerSchema("일괄 수정할 갯수 ", Format = "int")]
        public int? UpdateTargetCount { get; set; }
        [SwaggerSchema("일괄 변경할 내용")]
        public RequestHelpDatailMultiPutParam RequestHelpDatailPutParam { get; set; } = null!;
    }

    public class RequestHelpDatailMultiPutParam
    {
        [SwaggerSchema("도움요청자의 고유ID 0입력시 Null값입력", Format = "int")]
        public int? HelperMemId { get; set; }
        [SwaggerSchema("주차 등록상태", Format = "int")]
        public ReqDetailStatus? Status { get; set; }
        [SwaggerSchema("요청 수락 일자 UTC값 입력", Format = "date-time")]
        public DateTimeOffset? DiscountApplyDate { get; set; }
        [SwaggerSchema("None = 0 , Cafe = 1, Restaurant = 2", Format = "int")]
        public DiscountApplyType? DiscountApplyType { get; set; }
    }
}
