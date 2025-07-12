using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ParkingHelp.Models
{
    [Table("HelpOffer")]
    public class HelpOffer
    {
        public int Id { get; set; }
        public int HelperMemId { get; set; }
        public int Status { get; set; }
        public DateTime InsertDate { get; set; }
        public int ReqMemId { get; set; }
        public DateTime ReqDate { get; set; }
        public int? ReserveCarId { get; set; }

        public Member Helper { get; set; } = null!;
        public Member Requester { get; set; } = null!;
        public MemberCar ReserveCar { get; set; } = null!;
    }
}
