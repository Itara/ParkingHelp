using System.Security.Cryptography.X509Certificates;

namespace ParkingHelp.DTO
{
    public class RankingDTO
    {
        public int Id { get; set; }
        public int TotalHelpCount { get; set; }
        public string MemberName { get; set; } = string.Empty;

    }

    public class HelperHistoryDto
    {
        public int? HelperId { get; set; }
        public string HelperName { get; set; } = string.Empty;
        public int HelpCount { get; set; }
        
        public DateTimeOffset? LastHelpDate { get; set; }
        public List<HelpHistoryDto> RecentHelps { get; set; } = new();
    }

    public class HelpHistoryDto
    {
        public int ReqId { get; set; }
        public DateTimeOffset HelpDate { get; set; }
        
        public int RequestMemberId { get; set; }
    }
}
