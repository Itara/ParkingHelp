using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ParkingHelp.Models
{
    [Table("member_car")]
    public class MemberCar
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("member_id")]
        public int MemberId { get; set; }
        [Column("car_number")]
        public string CarNumber { get; set; } = string.Empty;
        [Column("create_date")]
        public DateTime CreateDate { get; set; } = DateTime.UtcNow;
        [Column("update_date")]
        public DateTime UpdateDate { get; set; } = DateTime.UtcNow;
        [JsonIgnore]
        public Member Member { get; set; } = null!;
        [JsonIgnore]
        public ICollection<ReqHelp> ReqHelps { get; set; } = new List<ReqHelp>();
    }
}
