using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace ParkingHelp.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HelpStatus
    {
        [EnumMember(Value = "Waiting (대기)")]
        Waiting = 0, // 대기
        [EnumMember(Value = "Check (주차요청도움 확인상태)")]
        Check = 1, // 주차요청도움 확인상태
        [EnumMember(Value = "Completed (주차등록완료)")]
        Completed = 2, // 주차등록완료
    }
    public enum ReqDetailStatus
    {
        [EnumMember(Value = "Waiting (대기)")]
        Waiting = 0, // 대기
        [EnumMember(Value = "Check (확인)")]
        Check = 1, // 대기
        [EnumMember(Value = "Completed (주차등록완료)")]
        Completed = 2, // 주차등록완료
    }
    public enum DiscountApplyType
    {
        [EnumMember(Value = "적용안됨")]
        None = 0,
        [EnumMember(Value = "카페")]
        Cafe = 1,
        [EnumMember(Value = "식당")]
        Restaurant = 2,
    }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RankingOrderType
    {
        [EnumMember(Value = "Ascending (오름차순)")]
        Ascending = 0, // 오름차순
        [EnumMember(Value = "Descending (내림차순)")]
        Descending = 1, // 내림차순
    }
    public enum DiscountJobType
    {
        ApplyDiscount,
        CheckFeeOnly
    }
    public enum DiscountJobPriority
    {
        High = 0, // 즉시 할인권 적용
        Medium = 50, // 퇴근 등록 차량
        Low = 100 // 배치 시간에 맞춰서 할인권 적용
    }

    public enum DiscountTicket
    {
        [Description("30분")]
        Min30 = 30,
        [Description("1시간")]
        Hour1 = 60,
        [Description("4시간")]
        Hour4 = 240
    }

    public enum DisCountResultType
    {
        Success = 0, // 성공
        SuccessButFee , // 성공했지만 요금 남아있음
        NotFound , // 입차기록이없음
        AlreadyUse ,  // 이미 할인권 사용
        CarMoreThanTwo, //차량정보가 2대 이상
        NoFee, // 결제할 금액없음
        NoUseTicket, // 할인권 없음
        Error = 99// 오류 발생
    }

    public enum HelpOfferType
    {
        OfferOnly = 0, // 제안 등록만
        ImmediateComplete = 1 // 제안 등록과 완료를 동시에 처리
    }
    public enum ProtocolType
    {
        [Description("Ping")]
        Ping = 0,
        [Description("HelpOfferRegist")]
        HelpOfferRegist,
        [Description("HelpOfferUpdate")]
        HelpOfferUpdate,
        [Description("HelpOfferDelete")]
        HelpOfferDelete,
        [Description("ReqHelpRegist")]
        ReqHelpRegist,
        [Description("ReqHelpUpdate")]
        ReqHelpUpdate,
        [Description("ReqHelpDelete")]
        ReqHelpDelete,
        [Description("ReqHelpDetailDelete")]
        ReqHelpDetailDelete,
    }

    public enum HelpHistoryType
    {
        [Description("도움요청")]
        HelpRequest = 0,
        [Description("도움제공")]
        HelpOffer = 1,
    }
}
