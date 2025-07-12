namespace ParkingHelp.DB.QueryCondition
{
    public class MemberAddParam
    {
        public string memberLoginId { get; set; } = string.Empty; // 회원 ID
        public string password { get; set; } = string.Empty; // 회원 비번
        public string memberName { get; set; } = string.Empty; // 회원명
        public string carNumber { get; set; } = string.Empty; // 차량 번호
    }

    public class MemberGetParam
    {
        public string memberLoginId { get; set; } = string.Empty; // 회원 ID
        public string memberName { get; set; } = string.Empty; // 회원명
        public string carNumber { get; set; } = string.Empty; // 차량 번호
    }

    public class MemberUpdateParam
    {
        public string memberId { get; set; } = string.Empty; // 회원 ID
        public string password { get; set; } = string.Empty; // 회원 비번
        public string memberName { get; set; } = string.Empty; // 회원명
        
    }

    public class  MemberDeleteParam
    {
        public string memberId { get; set; } = string.Empty; // 회원 ID
    }
}
