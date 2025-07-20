namespace ParkingHelp.DTO
{
    public class FavoriteMemberDTO
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public int FavoriteMemberId { get; set; }
        public string FavoriteMemberName { get; set; } = string.Empty;
        public DateTimeOffset CreateDate { get; set; }
        public DateTimeOffset UpdateDate { get; set; }
    }

    public class FavoriteMemberListDTO
    {
        public int Id { get; set; }
        public int FavoriteMemberId { get; set; }
        public string FavoriteMemberName { get; set; } = string.Empty;
        public DateTimeOffset CreateDate { get; set; }
    }
}
