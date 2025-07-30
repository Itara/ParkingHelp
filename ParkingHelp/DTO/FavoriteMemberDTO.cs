namespace ParkingHelp.DTO
{
    public class FavoriteMemberDTO
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public int FavoriteMemberId { get; set; }
        public string FavoriteMemberName { get; set; } = string.Empty;
        public string DelYn { get; set; } = string.Empty;
    }

    public class FavoriteMemberListDTO
    {
        public string FavoriteMemberName { get; set; } = string.Empty;
        public int FavoriteMemberId { get; set; }
        public string CarNumber { get; set; } = string.Empty;
    }
}
