using Microsoft.EntityFrameworkCore;
using ParkingHelp.Models;

namespace ParkingHelp.DB
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<MemberModel> Members { get; set; }
        public DbSet<MemberCarModel> MemberCars { get; set; }
        public DbSet<ReqHelpModel> ReqHelps { get; set; }
        public DbSet<ReqHelpDetailModel> ReqHelpsDetail { get; set; }
        public DbSet<HelpOfferModel> HelpOffers { get; set; }
        public DbSet<HelpOfferDetailModel> HelpOffersDetail { get; set; }
        public DbSet<FavoriteMemberModel> FavoriteMembers { get; set; }
        public DbSet<HelpHistoryModel> HelpHistories { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // DateTimeOffset 컬럼들을 timestamp with time zone으로 강제 매핑
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties()
                    .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                {
                    property.SetColumnType("timestamp with time zone");
                }
            }

            modelBuilder.Entity<MemberCarModel>(entity =>
            {
                entity.Property(e => e.CreateDate)
                      .HasColumnType("timestamp with time zone");
            });

            // 테이블명 설정 ( PostgreSQL은 테이블 명을 소문자로 하는게 네이밍 규칙이라 소문자로 정의)

            modelBuilder.Entity<MemberModel>().ToTable("member");
            modelBuilder.Entity<MemberCarModel>().ToTable("member_car");
            modelBuilder.Entity<ReqHelpModel>().ToTable("req_help");
            modelBuilder.Entity<ReqHelpDetailModel>().ToTable("req_help_detail");
            modelBuilder.Entity<HelpOfferModel>().ToTable("help_offer");
            modelBuilder.Entity<HelpOfferDetailModel>().ToTable("help_offer_detail");
            modelBuilder.Entity<FavoriteMemberModel>().ToTable("member_favorites");
            modelBuilder.Entity<HelpHistoryModel>().ToTable("help_history");


            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties()
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                {
                    property.SetColumnType("timestamp with time zone");
                }
            }

            //Member ↔ MemberCar (1:N)
            modelBuilder.Entity<MemberCarModel>()
                .HasOne(mc => mc.Member)
                .WithMany(m => m.Cars)
                .HasForeignKey(mc => mc.MemberId)
                .OnDelete(DeleteBehavior.Cascade);


            modelBuilder.Entity<MemberCarModel>(entity =>
            {
                entity.Property(e => e.CreateDate)
                      .HasColumnType("timestamp with time zone");
            });

            // ReqHelp ↔ Member (요청자)
            modelBuilder.Entity<ReqHelpModel>()
                .HasOne(r => r.HelpReqMember)
                .WithMany(m => m.HelpRequests)
                .HasForeignKey(r => r.HelpReqMemId)
                .OnDelete(DeleteBehavior.Cascade);

            // ReqHelpDetail ↔ HelperMember (요청 도와준사람)
            modelBuilder.Entity<ReqHelpDetailModel>()
                .HasOne(r => r.HelperMember)
                .WithMany(m => m.ReqHelpDetailHelper)
                .HasForeignKey(r => r.HelperMemberId)
                .OnDelete(DeleteBehavior.SetNull);

            // ReqHelpDetail ↔ ReqHelp (1:N)
            modelBuilder.Entity<ReqHelpDetailModel>()
                .HasOne(d => d.ReqHelps)
                .WithMany(r => r.HelpDetails)
                .HasForeignKey(d => d.Req_Id)
                .OnDelete(DeleteBehavior.Cascade);


            // HelpOffer ↔ Member (도와주는 사람)
            modelBuilder.Entity<HelpOfferModel>()
                .HasOne(h => h.HelperMember)
                .WithMany(m => m.HelpOffers)
                .HasForeignKey(h => h.HelperMemId)
                .OnDelete(DeleteBehavior.Cascade);

            // HelpOffer ↔ HelpOfferDetail (1:N)
            modelBuilder.Entity<HelpOfferDetailModel>()
                .HasOne(d => d.HelpOffer)
                .WithMany(r => r.HelpDetails)
                .HasForeignKey(d => d.HelpOfferId)
                .OnDelete(DeleteBehavior.Cascade);

            //helpofferdetail ↔ Member (요청자) 
            modelBuilder.Entity<HelpOfferDetailModel>()
               .HasOne(d => d.RequestMember)
               .WithMany(r => r.HelpOffersDetail)
               .HasForeignKey(d => d.RequestMemberId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HelpHistoryModel>()
            .HasOne(h => h.HelperMember)
            .WithMany()
            .HasForeignKey(h => h.HelpMemberId)
            .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<HelpHistoryModel>()
                .HasOne(h => h.ReceiveMember)
                .WithMany()
                .HasForeignKey(h => h.ReceiveHelpMemberId)
                .OnDelete(DeleteBehavior.SetNull);

            // // FavoriteMember ↔ Member (즐겨찾기 하는 회원)
            // modelBuilder.Entity<FavoriteMemberModel>()
            //     .HasOne(f => f.Member)
            //     .WithMany()
            //     .HasForeignKey(f => f.MemberId)
            //     .OnDelete(DeleteBehavior.Cascade);

            // // FavoriteMember ↔ Member (즐겨찾기 당하는 회원)
            // modelBuilder.Entity<FavoriteMemberModel>()
            //     .HasOne(f => f.FavoriteMember)
            //     .WithMany()
            //     .HasForeignKey(f => f.FavoriteMemberId)
            //     .OnDelete(DeleteBehavior.Cascade);

        }
    }
}
