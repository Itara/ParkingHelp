using ParkingHelp.Models;

namespace ParkingHelp.DTO
{
    public class HelpMemberDto
    {
        public int Id { get; set; }
        public string HelperName { get; set; } = string.Empty;
        public string? HelperEmail { get; set; } = string.Empty;
        public string? SlackId { get; set; } = string.Empty;
    }
    public class HelpRequesterDto
    {
        public int Id { get; set; }
        public string HelpRequesterName { get; set; } = string.Empty;
        public string? RequesterEmail { get; set; } = string.Empty;
        public string? SlackId { get; set; } = string.Empty;
        public ReqHelpCarDto? ReqHelpCar { get; set; }
    }

    public class ReqHelpCarDto
    {
        public int Id { get; set; }
        public string CarNumber { get; set; } = string.Empty;
    }

    public class ReqHelpDto
    {
        public int Id { get; set; }
        public DateTimeOffset ReqDate { get; set; }
        public HelpRequesterDto HelpRequester { get; set; } = null!;
        public RequestHelpStatus? Status { get; set; }
        public string? UpdateSlackThreadTs { get; set; } = null!;
        public int TotalDisCount { get; set; }
        public int? ApplyDisCount { get; set; }

        public List<ReqHelpDetailDto> HelpDetails { get; set; } = new();

    }

}