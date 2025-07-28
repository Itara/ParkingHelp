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
        public int? HelpReqMemId { get; set; }
        /// <summary>
        /// 요청상태
        /// </summary>
        [SwaggerSchema("요청상태 0:대기 , 1:요청확인 , 2 :주차등록 완료", Format = "int")]
        public HelpStatus? Status { get; set; }
        [SwaggerSchema("요청내역 하위 상태 조회", Format = "int")]
        public ReqDetailStatus? ReqDetailStatus { get; set; }
        /// <summary>
        /// 요청날짜
        /// </summary>
        /// <param name="fromDate">조회 시작일 (기본값: 오늘 0시)</param>
        [SwaggerSchema("조회 시작일 (기본값: 오늘 00:00) UTC", Format = "date-time")]
        public DateTimeOffset? FromReqDate { get; set; }
        [SwaggerSchema("조회 시작일 (기본값: 오늘 23:59) UTC", Format = "date-time")]
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
    public class RequestHelpPutParam
    {
        [SwaggerSchema("요청상태 0:대기 , 1:진행중 , 2 :주차등록 완료", Format = "int")]
        public HelpStatus? Status { get; set; }
        [SwaggerSchema("요청 할인권 갯수", Format = "int")]
        public int? DiscountApplyCount { get; set; }
        public int? DisCountAcceptedCount { get; set; }
        public List<RequestHelpDatailPutParam>? RequestHelpDetail { get; set; }
    }

    public class RequestHelpDetailParam
    {
        [SwaggerSchema("요청상태 0:대기 , 1:요청확인 , 2 :주차등록 완료", Format = "int")]
        public HelpStatus? Status { get; set; }
        [SwaggerSchema("요청 할인권 갯수", Format = "int")]
        public int? DiscountApplyCount { get; set; }
        public List<RequestHelpDatailPutParam>? RequestHelpDetail { get; set; }
    }

    public class RequestHelpDatailPutParam
    {
        [SwaggerSchema("", Format = "int")]
        public int Id { get; set; }
        [SwaggerSchema("도움요청자의 고유ID 0입력시 Null값입력", Format = "int")]
        public int? HelperMemId { get; set; }
        [SwaggerSchema("주차 등록상태", Format = "int")]
        public ReqDetailStatus? Status { get; set; }
        [SwaggerSchema("요청 수락 일자 UTC값 입력", Format = "date-time")]
        public DateTimeOffset? DiscountApplyDate { get; set; }
        [SwaggerSchema("None = 0 , Cafe = 1, Restaurant = 2", Format = "int")]
        public DiscountApplyType? DiscountApplyType { get; set; }
    }


    public class RequestHelpMultiPutParam
    {
        [SwaggerSchema("요청상태 0:대기 , 1:진행중 , 2 :주차등록 완료", Format = "int")]
        public HelpStatus? Status { get; set; }
        [SwaggerSchema("요청 할인권 갯수", Format = "int")]
        public int? DiscountApplyCount { get; set; }
        public int? DisCountAcceptedCount { get; set; }
        public List<RequestHelpDatailMultiPutParam>? RequestHelpDetail { get; set; }
    }

 

   
}
