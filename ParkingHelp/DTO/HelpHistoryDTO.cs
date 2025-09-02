using ParkingHelp.Models;

namespace ParkingHelp.DTO
{
    public class HelpHistoryDTO
    {
        public int Id { get; set; }

        public HelpHistoryType HelpType { get; set; }

        public int? HelperMemberId { get; set; }
        public string? HelperMemberName { get; set; }   // Navigation에서 꺼낸 값

        public int? ReceiveHelpMemberId { get; set; }
        public string? ReceiveMemberName { get; set; }  // Navigation에서 꺼낸 값

        public DiscountApplyType DiscountApplyType { get; set; }

        public int? DetailId { get; set; }

        public DateTimeOffset HelpCompleteDate { get; set; }
    }

}
