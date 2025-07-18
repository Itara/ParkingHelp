using ParkingHelp.Models;

namespace ParkingHelp.DTO
{
    public class ReqHelpDetailDto
    {
        public int Id { get; set; }
        public ReqDetailStatus ReqDetailStatus { get; set; }
        public DiscountApplyType DiscountApplyType { get; set; }
        public DateTimeOffset? DiscountApplyDate { get; set; }
        public DateTimeOffset? InsertDate { get; set; }
        public HelpMemberDto? Helper { get; set; }
        public string? SlackThreadTs { get; set; }
    }
}
