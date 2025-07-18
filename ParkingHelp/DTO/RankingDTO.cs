namespace ParkingHelp.DTO
{
    public class RankingDTO
    {
        public int Id { get; set; }
        public int TotalHelpCount { get; set; }
        public string MemberName { get; set; } = string.Empty;

    }

    public class HelperStatsDto
    {
        public int? HelperId { get; set; }
        public string HelperName { get; set; } = string.Empty;
        public int HelpCount { get; set; }
        public DateTimeOffset? FirstHelpDate { get; set; }
        public DateTimeOffset? LastHelpDate { get; set; }
        public List<RecentHelpDto> RecentHelps { get; set; } = new();
    }

    public class RecentHelpDto
    {
        public int ReqId { get; set; }
        public DateTimeOffset? InsertDate { get; set; }
        public int Status { get; set; }
    }
}
