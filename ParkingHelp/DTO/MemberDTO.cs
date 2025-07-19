namespace ParkingHelp.DTO
{
    public class MemberDto
    {
        public int Id { get; set; }
        public string MemberLoginId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Email { get; set; }
        public string? SlackId { get; set; }
        public List<MemberCarDTO>? Cars { get; set; }
        public List<ReqHelpDto>? RequestHelpHistory { get; set; } //Login할때 기본적으로 가져올 자신의 요청내역

        public List<HelpOfferDTO>? HelpOfferHistory { get; set; } //Login할때 기본적으로 d가져올 자신의 도움내역

    }

}
