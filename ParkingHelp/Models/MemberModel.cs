using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ParkingHelp.Models
{
    [Table("member")]
    public class MemberModel
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("member_login_id")]
        public string MemberLoginId { get; set; } = string.Empty;
        //[Column("password")]
        //public string Password { get; set; } = string.Empty;
        [Column("member_name")]
        public string MemberName { get; set; } = string.Empty;
        [Column("email")]
        public string Email { get; set; } = string.Empty;
        [Column("create_date", TypeName = "timestamp with time zone")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]  
        public DateTimeOffset CreateDate { get; set; } = DateTimeOffset.UtcNow;
        [Column("slack_id")]
        public string? SlackId { get; set; }

        public ICollection<MemberCarModel> Cars { get; set; } = new List<MemberCarModel>();
        
        public ICollection<ReqHelpModel> HelpRequests { get; set; } = new List<ReqHelpModel>();
        public ICollection<ReqHelpDetailModel> ReqHelpDetailHelper { get; set; } = new List<ReqHelpDetailModel>();

        public ICollection<HelpOfferModel> HelpOffers { get; set; } = new List<HelpOfferModel>();
        public ICollection<HelpOfferDetailModel> HelpOffersDetail { get; set; } = new List<HelpOfferDetailModel>();


    }
}
