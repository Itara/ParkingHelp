using AutoMapper;
using ParkingHelp.Models;
namespace ParkingHelp.DTO
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<MemberModel, MemberDto>()
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.MemberName));
            CreateMap<MemberCarModel, MemberCarDTO>();
            CreateMap<HelpOfferModel, HelpOfferDTO>();
            CreateMap<ReqHelpModel, ReqHelpDto>();
        }
    }
}
