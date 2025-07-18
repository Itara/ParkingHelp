using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkingHelp.Models
{
    [Table("req_help_detail")]
    public class ReqHelpDetailModel
    {
        [Key]
        [Column("id")]
        public int Id { get;set; }
        [ForeignKey("ReqHelp")]
        [Column("req_id")]
        public int Req_Id { get; set; }
        [Column("status")]
        public ReqDetailStatus ReqDetailStatus { get; set; }
        [Column("helper_mem_id")]
        public int? HelperMemberId { get; set; }
        [Column("discount_apply_date")]
        public DateTimeOffset? DiscountApplyDate { get; set; }
        [Column("discount_apply_type")]
        public DiscountApplyType DiscountApplyType { get; set; }
        [Column("insert_date")]
        public DateTimeOffset? InsertDate { get;set; }
        [Column("slack_thread_ts")]
        public string? SlackThreadTs { get;set; }

        public ReqHelpModel ReqHelps { get; set; } = null!;
        public MemberModel? HelperMember { get; set; }
    }
}
