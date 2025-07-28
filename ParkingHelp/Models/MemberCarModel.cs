using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ParkingHelp.Models
{
    [Table("member_car")]
    public class MemberCarModel
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("member_id")]
        public int MemberId { get; set; }
        [Column("car_number")]
        public string CarNumber { get; set; } = string.Empty;
        [Column("create_date", TypeName = "timestamp with time zone")]
        public DateTimeOffset CreateDate { get; set; } = DateTimeOffset.UtcNow;
        [Column("update_date",TypeName = "timestamp with time zone")]
        public DateTimeOffset UpdateDate { get; set; } = DateTimeOffset.UtcNow;
        public MemberModel Member { get; set; } = null!;
    }
}
