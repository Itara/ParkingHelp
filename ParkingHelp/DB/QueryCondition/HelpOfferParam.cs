using ParkingHelp.DTO;
using ParkingHelp.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;

namespace ParkingHelp.DB.QueryCondition
{
    public class HelpOfferParam
    {
        /// <summary>도움을 제공한 사용자 ID</summary>
        [SwaggerSchema("주차 등록해줄사람 고유Id", Format = "int")]
        //[DefaultValue(1)]
        public int? HelperMemId { get; set; }  

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
        public DateTime? FromReqDate { get; set; }
        [SwaggerSchema("조회 시작일 (기본값: 오늘 23:59)", Format = "date-time")]
        /// <summary>요청일 범위 끝</summary>
        public DateTime? ToReqDate { get; set; }

    }
    public class HelpOfferPostParam
    {
        /// <summary>도움을 제공한 사람</summary>
        public int HelpOfferId { get; set; }
        /// <summary>상태 값</summary>
        [DefaultValue(0)]
        public HelpStatus? Status { get; set; }
        /// <summary>생성 날짜</summary>
        public DateTime InsertDate { get; set; }
    }
    public class HelpOfferPutParam
    {
        /// <summary>도움을 요청한 사람</summary>
        //public int? HelpMemId { get; set; }
        /// <summary>대기 누른 시간</summary>
        //public DateTime? HelpDate { get; set; }

        [SwaggerSchema("요청상태 0:대기 , 1:요청확인 , 2 :주차등록 완료", Format = "int")]
        //[DefaultValue(0)]
        public HelpStatus? Status { get; set; }

        /// <summary>차 번호</summary>
        //public string? CarNumber { get; set; }

        /// <summary>완료 누른시간</summary>
        //public DateTime? ConfirmDate { get; set; }
        public List<HelpOfferDetailPutParam>? HelpOfferDetail { get; set; }
    }

    public class HelpOfferDetailParam
    {

        [SwaggerSchema("요청상태 0:대기 , 1:요청확인 , 2 :주차등록 완료", Format = "int")]
        [DefaultValue(0)]
        public HelpStatus? Status { get; set; }
        [SwaggerSchema("요청 할인권 갯수", Format = "int")]
        public int? DiscountApplyCount { get; set; }
    }

    public class HelpOfferDetailPutParam
    {
        public int Id { get; set; }
        //[SwaggerSchema("도움요청자의 고유ID", Format = "int")]
        //public int? HelperMemId { get; set; }
        public ReqDetailStatus? Status { get; set; }
        public DateTimeOffset? DiscountApplyDate { get; set; }
        public DiscountApplyType? DiscountApplyType { get; set; }
        //public List<HelpOfferDetailParam>? HelpOfferDetail { get; set; }
        public DateTimeOffset? RequestDate { get; set; }
        public int? ReqMemberId { get; set; }


    }
}