namespace ParkingHelp.Models
{
    public class ParkingAccount
    {
        public string Id { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool IsMain { get; set; }
    }
}
