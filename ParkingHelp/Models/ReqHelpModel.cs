using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkingHelp.Models
{
    [Table("req_help")]
    public class ReqHelpModel
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("help_req_mem_id")]
        public int HelpReqMemId { get; set; }
        public MemberModel HelpReqMember { get; set; } = null!;

        [Column("req_car_id")]
        public int? ReqCarId { get; set; }
        public MemberCarModel ReqCar { get; set; } = null!;

        [Column("discount_total_count")]
        public int DiscountTotalCount { get; set; }
        [Column("discount_apply_count")]
        public int? DiscountApplyCount { get; set; }
        [Column("status")]
        public RequestHelpStatus Status { get; set; }
        [Column("req_date")]
        public DateTimeOffset ReqDate { get; set; } = DateTimeOffset.UtcNow;
        [Column("slack_thread_ts")]
        public string? SlackThreadTs { get; set; }

        public ICollection<ReqHelpDetailModel> HelpDetails { get; set; } = new List<ReqHelpDetailModel>();
    }
}
