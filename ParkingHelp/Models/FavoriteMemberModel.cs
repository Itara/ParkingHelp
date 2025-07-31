using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ParkingHelp.Models
{
    [Table("member_favorites")]
    public class FavoriteMemberModel
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("member_id")]
        public int MemberId { get; set; }

        [Column("favorite_member_id")]
        public int FavoriteMemberId { get; set; }

        [Column("del_yn")]
        public string DelYn { get; set; } = "N";

        [Column("create_date")]
        public DateTimeOffset CreateDate { get; set; } = DateTimeOffset.UtcNow;

        [Column("update_date")]
        public DateTimeOffset UpdateDate { get; set; } = DateTimeOffset.UtcNow;

        // Navigation Properties
        public MemberModel Member { get; set; } = null!;  // 즐겨찾기 하는 회원
        public MemberModel FavoriteMember { get; set; } = null!;  // 즐겨찾기 당하는 회원
    }
}
