using ParkingHelp.Models;
using Swashbuckle.AspNetCore.Annotations;

//using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;

namespace ParkingHelp.DB.QueryCondition
{
   
    public class RequestHelpGetParam
    {
        /// <summary>도움 요청자 ID</summary>
       // [SwaggerSchema("도움 요청자 고유Id", Format = "int")]
        [DefaultValue(1)]
        public int? HelpReqMemId { get; set; }

        /// <summary>도움을 제공한 사용자 ID</summary>
        //[SwaggerSchema("주차 등록해줄사람 고유Id", Format = "int")]
        [DefaultValue(1)]
        public int? HelperMemId { get; set; }

        /// <summary>요청 차량 ID</summary>
        //[SwaggerSchema("차량번호", Format = "string")]
        [DefaultValue("10저3519")]
        public string? ReqCarNumber { get; set; }

        /// <summary>
        /// 요청상태
        /// </summary>
        [SwaggerSchema("요청상태 0:대기 , 1:요청확인 , 2 :주차등록 완료", Format = "int")]
        [DefaultValue(0)]
        public CarHelpStatus? Status { get; set; }

        /// <summary>
        /// 요청날짜
        /// </summary>
        /// <param name="fromDate">조회 시작일 (기본값: 오늘 0시)</param>
        [SwaggerSchema("조회 시작일 (기본값: 오늘 00:00)", Format = "date-time")]
        public DateTime? FromReqDate { get; set; }
        [SwaggerSchema("조회 시작일 (기본값: 오늘 23:59)", Format = "date-time")]
        /// <summary>요청일 범위 끝</summary>
        public DateTime? ToReqDate { get; set; }
    }

    public class RequestHelpPostParam
    {
        /// <summary>도움 요청자 ID</summary>
        public int? HelpReqMemId { get; set; }
        [SwaggerSchema("차량 고유ID", Format = "int")]
        [DefaultValue(1)]
        public int? CarId { get; set; }
        /// <summary>
        /// 요청 차량 번호
        /// </summary>
        public string? CarNumber { get; set; }
    }

    public class RequestHelpPutParam
    {
        /// <summary>도움을 제공한 사용자 ID</summary>
        public int? HelperMemId { get; set; }
        [SwaggerSchema("요청상태 0:대기 , 1:요청확인 , 2 :주차등록 완료", Format = "int")]
        [DefaultValue(0)]
        public CarHelpStatus? Status { get; set; }
   
        public DateTime? ConfirmDate { get; set; }
        public DateTime? HelpDate { get; set; }// 도움 제공 날짜
    }
}
