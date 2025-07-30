using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ParkingHelp.Models
{
    [Table("help_offer")]
    public class HelpOfferModel
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }
        [Column("helper_mem_id")]
        public int HelperMemId { get; set; }
        public MemberModel HelperMember { get; set; } = null!;

        [Column("status")]
        public HelpStatus Status { get; set; }
        [Column("helper_service_date", TypeName = "timestamp with time zone")]
        public DateTimeOffset? HelerServiceDate { get; set; }

        [Column("discount_total_count")]
        public int DiscountTotalCount { get; set; }
        [Column("discount_apply_count")]
        public int? DiscountApplyCount { get; set; }

        // 직접 완료를 등록 했는지 여부
        [Column("help_offer_type")]
        public HelpOfferType HelpOfferType { get; set; }

        public ICollection<HelpOfferDetailModel> HelpDetails { get; set; } = new List<HelpOfferDetailModel>();

        [Column("slack_thread_ts")]
        public string? SlackThreadTs { get; set; }
    }
}
