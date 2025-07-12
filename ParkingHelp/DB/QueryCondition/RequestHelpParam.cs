using ParkingHelp.Models;

namespace ParkingHelp.DB.QueryCondition
{
   
    public class RequestHelpGetParam
    {
        /// <summary>도움 요청자 ID</summary>
        public int? HelpReqMemId { get; set; }

        /// <summary>도움을 제공한 사용자 ID</summary>
        public int? HelperMemId { get; set; }

        /// <summary>요청 차량 ID</summary>
        public string? ReqCarNumber { get; set; }

        /// <summary>요청 상태 (예: 0:대기, 1:매칭, 2:완료 등)</summary>
        public CarHelpStatus? Status { get; set; }

        /// <summary>요청일 범위 시작</summary>
        public DateTime? FromReqDate { get; set; }

        /// <summary>요청일 범위 끝</summary>
        public DateTime? ToReqDate { get; set; }
    }

    public class RequestHelpPostParam
    {
        /// <summary>도움 요청자 ID</summary>
        public int? HelpReqMemId { get; set; }

        /// <summary>주차요청상태</summary>
        public CarHelpStatus? Status { get; set; }
        /// <summary>
        /// 요청 차량 번호
        /// </summary>
        public string? CarNumber { get; set; }
    }

    public class RequestHelpPutParam
    {
        /// <summary>도움을 제공한 사용자 ID</summary>
        public int? HelperMemId { get; set; }

        public CarHelpStatus? Status { get; set; }
   
        public DateTime? ConfirmDate { get; set; }
        public DateTime? HelpDate { get; set; }// 도움 제공 날짜
    }
}
