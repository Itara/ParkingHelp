using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ParkingHelp.Models
{
    [Table("HelpOffer")]
    public class HelpOffer
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("helper_mem_id")]
        public int HelperMemId { get; set; }
        [Column("status")]
        public CarHelpStatus? Status { get; set; }
        [Column("insert_date")]
        public DateTime InsertDate { get; set; }
        [Column("req_mem_id")]
        public int ReqMemId { get; set; }
        [Column("req_date")]
        public DateTime ReqDate { get; set; }
        [Column("reserve_car_id")]
        public int? ReserveCarId { get; set; }

        public Member Helper { get; set; } = null!;
        public Member? Requester { get; set; } = null!;
        public MemberCar? ReserveCar { get; set; } = null!;
    }
}
