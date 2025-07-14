namespace ParkingHelp.DB.DTO
{
    public class HelpHistoryDTO
    {
        public int HelperMemId { get; set; }
        public int TotalCount { get; set; }
        public List<ReqHelpDto> ReqHelps { get; set; } = new();
        public List<HelpOfferDTO> HelpOffers { get; set; } = new();
    }

}
