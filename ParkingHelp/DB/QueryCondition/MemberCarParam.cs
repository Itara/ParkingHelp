using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;

namespace ParkingHelp.DB.QueryCondition
{
    public class MemberCarGetParam
    {
        public int? MemberId { get; set; } // 회원 ID
        public string? CarNumber { get; set; } = string.Empty; // 차량 번호
    }

    public class MemberCarPostParam
    {
        public int MemberId { get; set; } // 회원 ID
        public string CarNumber { get; set; } = string.Empty; // 차량 번호
    }

    
}
