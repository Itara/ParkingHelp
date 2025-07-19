using ParkingHelp.Models;
using Swashbuckle.AspNetCore.Annotations;

//using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;

namespace ParkingHelp.DB.QueryCondition
{
   
    public class RequestHelpGetParam
    {
        /// <summary>도움 요청자 ID</summary>
        [SwaggerSchema("도움 요청자 고유Id", Format = "int")]
        [DefaultValue(1)]
        public int? HelpReqMemId { get; set; }
        /// <summary>
        /// 요청상태
        /// </summary>
        [SwaggerSchema("요청상태 0:대기 , 1:요청확인 , 2 :주차등록 완료", Format = "int")]
        [DefaultValue(0)]
        public HelpStatus? Status { get; set; }

        /// <summary>
        /// 요청날짜
        /// </summary>
        /// <param name="fromDate">조회 시작일 (기본값: 오늘 0시)</param>
        [SwaggerSchema("조회 시작일 (기본값: 오늘 00:00)", Format = "date-time")]
        public DateTimeOffset? FromReqDate { get; set; }
        [SwaggerSchema("조회 시작일 (기본값: 오늘 23:59)", Format = "date-time")]
        /// <summary>요청일 범위 끝</summary>
        public DateTimeOffset? ToReqDate { get; set; }
    }

    public class RequestHelpPostParam
    {
        [SwaggerSchema("도움요청자의 고유ID", Format = "string")]
        public int? HelpReqMemId { get; set; }
        [SwaggerSchema("할인요청 갯수", Format = "string")]
        public int TotalDisCount { get; set; }
    }

    public class RequestHelpDetailParam
    {
      
        [SwaggerSchema("요청상태 0:대기 , 1:요청확인 , 2 :주차등록 완료", Format = "int")]
        [DefaultValue(0)]
        public HelpStatus? Status { get; set; }
        [SwaggerSchema("요청 할인권 갯수", Format = "int")]
        public int? DiscountTotalCount { get; set; }
        [SwaggerSchema("적용 할인권 갯수", Format = "int")]
        public int? DiscountApplyCount { get; set; }
        public DateTimeOffset ReqDate { get; set; }
        public List<RequestHelpDatailParam>? RequestHelpDetail { get; set; }
    }

    public class RequestHelpDatailParam
    {
        public int Id { get; set; }
        public int Req_id { get; set; }
        [SwaggerSchema("도움요청자의 고유ID", Format = "int")]
        public int? HelperMemId { get; set; }
        public ReqDetailStatus? Status { get; set; }
        public DateTimeOffset? DiscountApplyDate { get; set; }
        public DiscountApplyType? DiscountApplyType { get; set; }
    }
}
