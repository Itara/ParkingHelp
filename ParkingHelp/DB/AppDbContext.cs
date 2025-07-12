using Microsoft.EntityFrameworkCore;
using ParkingHelp.Models;

namespace ParkingHelp.DB
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<Member> Members { get; set; }
        public DbSet<MemberCar> MemberCars { get; set; }
        public DbSet<ReqHelp> ReqHelps { get; set; }
        public DbSet<HelpOffer> HelpOffers { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 테이블명 설정 (대소문자 구분 방지)
            modelBuilder.Entity<Member>().ToTable("member").Property(m => m.CreateDate).ValueGeneratedOnAdd();
            modelBuilder.Entity<MemberCar>().ToTable("member_car");
            modelBuilder.Entity<ReqHelp>().ToTable("req_help");
            modelBuilder.Entity<HelpOffer>().ToTable("helpoffer");

            // ✅ UTC DateTime 컨벤션 적용 (원하면 전체 엔티티에)
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(DateTime)))
                {
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                        v => v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : v,
                        v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
                    ));
                }
            }

            // 관계 설정
            modelBuilder.Entity<MemberCar>()
                .HasOne(mc => mc.Member)
                .WithMany(m => m.Cars)
                .HasForeignKey(mc => mc.MemberId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ReqHelp>()
                .HasOne(r => r.HelpRequester)
                .WithMany(m => m.HelpRequests)
                .HasForeignKey(r => r.HelpReqMemId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ReqHelp>()
                .HasOne(r => r.Helper)
                .WithMany()
                .HasForeignKey(r => r.HelperMemId)
                .OnDelete(DeleteBehavior.SetNull);

            //modelBuilder.Entity<ReqHelp>()
            //    .HasOne(r => r.ReqCar)
            //    .WithMany(c => c.ReqHelps)
            //    .HasForeignKey(r => r.ReqCarId)
            //    .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HelpOffer>()
                .HasOne(h => h.Helper)
                .WithMany(m => m.HelpOffers)
                .HasForeignKey(h => h.HelperMemId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HelpOffer>()
                .HasOne(h => h.Requester)
                .WithMany()
                .HasForeignKey(h => h.ReqMemId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HelpOffer>()
             .HasOne(h => h.ReserveCar)
            .WithMany()
            .HasForeignKey(h => h.ReserveCarId)
            .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
