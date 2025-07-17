using ParkingHelp.Models;

namespace ParkingHelp.DTO
{
    public class HelpOfferDTO
    {
        public int Id { get; set; }
        public DateTimeOffset? HelpDate { get; set; }
        public HelpRequesterDto HelpRequester { get; set; } = null!;
        public DateTimeOffset RequestDate { get; set; } 
        public HelperDto? Helper { get; set; }
        public HelpReserveCarDto? ReqCar { get; set; } = null!;
        public RequestHelpStatus? Status { get; set; }
    }
   

    public class HelpReserveCarDto
    {
        public int Id { get; set; }
        public string CarNumber { get; set; } = string.Empty;
    }

}
