using ParkingHelp.Models;
using System.Text.Json.Serialization;

namespace ParkingHelp.DTO
{
    public class HelpOfferDTO
    {
        public int Id { get; set; }

        public HelpMemberDto Helper { get; set; } = null!;
        public HelpStatus Status { get; set; }
        public DateTimeOffset? HelperServiceDate { get; set; }
        public int DiscountTotalCount { get; set; }
        public int? DiscountApplyCount { get; set; }
        public List<HelpOfferDetailDTO>? HelpOfferDetail { get; set; }
        public string? SlackThreadTs { get; set; } = null!;
    }

    public class MyHelpOfferDTO
    {
        public int Id { get; set; }

        public HelpMemberDto Helper { get; set; } = null!;
        public HelpStatus Status { get; set; }
        public DateTimeOffset? HelperServiceDate { get; set; }
        public int DiscountTotalCount { get; set; }
        public int? DiscountApplyCount { get; set; }
        public List<HelpOfferDetailDTO>? HelpOfferDetail { get; set; }
        public string? SlackThreadTs { get; set; } = null!;
    }

    public class  HelpOfferDetailDTO
    {
        public int Id { get; set; }
        public HelpRequesterDto? HelpRequester { get; set; } 
        public ReqDetailStatus ReqDetailStatus { get; set; }
        public DateTimeOffset? DiscountApplyDate { get; set; }
        public DiscountApplyType DiscountApplyType { get; set; }
        public DateTimeOffset? RequestDate { get; set; }
    }


}
