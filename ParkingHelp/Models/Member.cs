using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkingHelp.Models
{
    [Table("member")]
    public class Member
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("member_login_id")]
        public string MemberLoginId { get; set; } = string.Empty;
        [Column("password")]
        public string Password { get; set; } = string.Empty;
        [Column("member_name")]
        public string MemberName { get; set; } = string.Empty;
        [Column("email")]
        public string Email { get; set; } = string.Empty;
        [Column("create_date")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]  
        public DateTime CreateDate { get; set; } = DateTime.UtcNow;

        public ICollection<MemberCar> Cars { get; set; } = new List<MemberCar>();
        public ICollection<ReqHelp> HelpRequests { get; set; } = new List<ReqHelp>();
        public ICollection<HelpOffer> HelpOffers { get; set; } = new List<HelpOffer>();
    }
}
