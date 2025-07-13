namespace ParkingHelp.DB.QueryCondition
{
    public class LoginParam
    {
        /// <summary>Login ID</summary>
        public string? MemberId { get; set; }
        public string? CarNumber { get; set; } = string.Empty; // 차량 번호
    }
}
