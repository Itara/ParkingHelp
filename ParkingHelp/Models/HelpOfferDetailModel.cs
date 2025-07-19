using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkingHelp.Models
{
    [Table("help_offer_detail")]
    public class HelpOfferDetailModel
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [ForeignKey("HelpOffer")]
        [Column("help_offer_id")]
        public int HelpOfferId { get; set; }

        [Column("status")]
        public HelpStatus Status { get; set; }

        [Column("request_mem_id")]
        public int? RequestMemberId { get; set; }

        [Column("discount_apply_date")]
        public DateTimeOffset? DiscountApplyDate { get; set; }

        [Column("discount_apply_type")]
        public DiscountApplyType DiscountApplyType { get; set; }

        [Column("request_date")]
        public DateTimeOffset? RequestDate { get; set; }

        [Column("slack_thread_ts")]
        public string? SlackThreadTs { get; set; }

        public HelpOfferModel HelpOffer { get; set; } = null!;
        public MemberModel? RequestMember { get; set; }
    }
}
