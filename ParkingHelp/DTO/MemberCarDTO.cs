namespace ParkingHelp.DTO
{
    public class MemberCarDTO
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public string CarNumber { get; set; } = null!;
    }
}
