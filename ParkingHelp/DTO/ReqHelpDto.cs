using ParkingHelp.Models;

namespace ParkingHelp.DTO
{
    public class HelpRequesterDto
    {
        public int Id { get; set; }
        public string HelpRequesterName { get; set; } = string.Empty;
        public string? RequesterEmail { get; set; } = string.Empty;
        public string? SlackId { get; set; } = string.Empty;
    }

    public class HelperDto
    {
        public int Id { get; set; }
        public string HelperName { get; set; } = string.Empty;
        public string? HelperEmail { get; set; } = string.Empty;
        public string? SlackId { get; set; } = string.Empty;
    }

    public class ReqHelpCarDto
    {
        public int Id { get; set; }
        public string CarNumber { get; set; } = string.Empty;
    }

    public class ReqHelpDto
    {
        public int Id { get; set; }
        public DateTime ReqDate { get; set; }
        public DateTime? HelpDate { get; set; }
        public HelpRequesterDto HelpRequester { get; set; } = null!;
        public HelperDto? Helper { get; set; }
        public ReqHelpCarDto? ReqCar { get; set; } = null!;
        public CarHelpStatus? Status { get; set; }
    }

}