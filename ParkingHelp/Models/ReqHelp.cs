using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkingHelp.Models
{
    [Table("req_help")]
    public class ReqHelp
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("help_req_mem_id")]
        public int HelpReqMemId { get; set; }
        [Column("req_car_id")]
        public int? ReqCarId { get; set; }
        [Column("status")]
        public CarHelpStatus Status { get; set; }
        [Column("helper_mem_id")]
        public int? HelperMemId { get; set; }
        [Column("car_number")]
        public string? carNumber { get; set; }
        [Column("req_date")]
        public DateTime ReqDate { get; set; } = DateTime.UtcNow;
        [Column("help_date")]
        public DateTime? HelpDate { get; set; }
        [Column("confirm_date")]
        public DateTime? ConfirmDate { get; set; }
        [Column("slack_thread_ts")]
        public string? SlackThreadTs { get; set; }

        public Member HelpRequester { get; set; } = null!;
        public Member? Helper { get; set; }
        public MemberCar ReqCar { get; set; } = null!;
    }
}
