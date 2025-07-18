using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ParkingHelp.Models
{
    [Table("HelpOffer")]
    public class HelpOfferModel
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }
        
        [Column("helper_mem_id")]
        [ForeignKey("HelperMember")]
        public int HelperMemId { get; set; }
        public MemberModel HelperMember { get; set; }

        [Column("status")]
        public RequestHelpStatus? Status { get; set; }
        [Column("insert_date")]
        public DateTimeOffset? InsertDate { get; set; }

        [ForeignKey("RequestMember")]
        [Column("req_mem_id")]
        public int ReqMemId { get; set; }
        public MemberModel RequestMember { get; set; }

        [Column("req_date")]
        public DateTimeOffset? ReqDate { get; set; }

        [Column("reserve_car_id")]
        [ForeignKey("ReserveCar")]
        public int? ReserveCarId { get; set; }
        public MemberCarModel? ReserveCar { get; set; } = null!;

        [Column("help_date")]
        public DateTimeOffset? HelpDate { get; set; }
        [Column("slack_thread_ts")]
        public string? SlackThreadTs { get; set; }

        public ICollection<ReqHelpDetailModel> HelpDetails { get; set; } = new List<ReqHelpDetailModel>();


        [Column("member_car_model_id")]
        public int? MemberCarModelId { get; set; }
    }
}
