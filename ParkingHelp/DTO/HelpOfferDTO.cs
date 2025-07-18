using ParkingHelp.Models;
using System.Text.Json.Serialization;

namespace ParkingHelp.DTO
{
    public class HelpOfferDTO
    {
        public int Id { get; set; }
        public DateTimeOffset? HelpOfferDate { get; set; }
        public HelpRequesterDto HelpRequester { get; set; } = null!;
        public DateTimeOffset RequestDate { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public HelpMemberDto? Helper { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public HelpReserveCarDto? ReqCar { get; set; } = null!;
        public RequestHelpStatus? Status { get; set; }
        public string HelpOffName { get; set; }
    }
   

    public class HelpReserveCarDto
    {
        public int Id { get; set; }
        public string CarNumber { get; set; } = string.Empty;
    }

}
