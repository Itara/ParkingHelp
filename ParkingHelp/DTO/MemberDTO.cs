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

    }

}
