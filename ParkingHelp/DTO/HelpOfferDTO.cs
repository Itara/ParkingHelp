using ParkingHelp.Models;

namespace ParkingHelp.DTO
{
    public class HelpOfferDTO
    {
        public int Id { get; set; }
        public DateTime? HelpDate { get; set; }
        public HelpRequesterDto HelpRequester { get; set; } = null!;
        public DateTime RequestDate { get; set; } 
        public HelperDto? Helper { get; set; }
        public HelpReserveCarDto? ReqCar { get; set; } = null!;
        public CarHelpStatus? Status { get; set; }
    }
   

    public class HelpReserveCarDto
    {
        public int Id { get; set; }
        public string CarNumber { get; set; } = string.Empty;
    }

}
