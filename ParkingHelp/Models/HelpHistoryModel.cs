using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ParkingHelp.Models
{
    [Table("help_history")]
    public class HelpHistoryModel
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("help_type")]
        public HelpHistoryType HelpType { get; set; }
        [Column("helper_member_id")]
        public int? HelpMemberId { get; set; }
        [Column("receive_help_member_id")]
        public int? ReceiveHelpMemberId { get; set; }
        [Column("help_place_type")]
        public DiscountApplyType DiscountApplyType { get; set; }
        [Column("help_detail_id")]
        public int DetailId { get; set; }
        [Column("help_complete_date")]
        public DateTimeOffset HelpCompleteDate { get; set; } = DateTimeOffset.UtcNow;
        [Column("status")]
        public int? status { get; set; }
        public MemberModel? HelperMember { get; set; }
        public MemberModel? ReceiveMember { get; set; } 

    }
}
