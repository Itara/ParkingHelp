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
        public ReqDetailStatus Status { get; set; }

        [Column("helper_mem_id")]
        public int? HelperMemberId { get; set; }

        [Column("discount_apply_date")]
        public DateTimeOffset? DiscountApplyDate { get; set; }

        [Column("discount_apply_type")]
        public DiscountApplyType DiscountApplyType { get; set; }

        [Column("insert_date")]
        public DateTimeOffset? InsertDate { get; set; }

        [Column("slack_thread_ts")]
        public string? SlackThreadTs { get; set; }

        public HelpOfferModel HelpOffer { get; set; } = null!;
        public MemberModel? HelperMember { get; set; }
    }
}
