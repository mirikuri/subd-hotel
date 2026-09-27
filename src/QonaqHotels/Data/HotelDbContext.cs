using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Models;

namespace QonaqHotels.Data;

public partial class HotelDbContext : DbContext
{
    public HotelDbContext(DbContextOptions<HotelDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Booking> Bookings { get; set; }

    public virtual DbSet<BookingRoom> BookingRooms { get; set; }

    public virtual DbSet<City> Cities { get; set; }

    public virtual DbSet<CommercialUnit> CommercialUnits { get; set; }

    public virtual DbSet<ContractUnit> ContractUnits { get; set; }

    public virtual DbSet<Country> Countries { get; set; }

    public virtual DbSet<Employee> Employees { get; set; }

    public virtual DbSet<Guest> Guests { get; set; }

    public virtual DbSet<Hotel> Hotels { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<InvoiceItem> InvoiceItems { get; set; }

    public virtual DbSet<LeaseContract> LeaseContracts { get; set; }

    public virtual DbSet<LoyaltyCard> LoyaltyCards { get; set; }

    public virtual DbSet<MeterReading> MeterReadings { get; set; }

    public virtual DbSet<Payroll> Payrolls { get; set; }

    public virtual DbSet<Penalty> Penalties { get; set; }

    public virtual DbSet<Position> Positions { get; set; }

    public virtual DbSet<Rate> Rates { get; set; }

    public virtual DbSet<Room> Rooms { get; set; }

    public virtual DbSet<RoomType> RoomTypes { get; set; }

    public virtual DbSet<Season> Seasons { get; set; }

    public virtual DbSet<Service> Services { get; set; }

    public virtual DbSet<ServiceCategory> ServiceCategories { get; set; }

    public virtual DbSet<ServiceOrder> ServiceOrders { get; set; }

    public virtual DbSet<Shift> Shifts { get; set; }

    public virtual DbSet<Stay> Stays { get; set; }

    public virtual DbSet<Tenant> Tenants { get; set; }

    public virtual DbSet<UnitType> UnitTypes { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UtilityMeter> UtilityMeters { get; set; }

    public virtual DbSet<UtilityTariff> UtilityTariffs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("audit_log_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.ChangedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("audit_log_user_id_fkey");
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("bookings_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.BookingDate).HasDefaultValueSql("now()");
            entity.Property(e => e.Status).HasDefaultValueSql("'новая'::character varying");

            entity.HasOne(d => d.Guest).WithMany(p => p.Bookings)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("bookings_guest_id_fkey");

            entity.HasOne(d => d.Hotel).WithMany(p => p.Bookings)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("bookings_hotel_id_fkey");
        });

        modelBuilder.Entity<BookingRoom>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("booking_rooms_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.GuestsCount).HasDefaultValue((short)1);
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Booking).WithMany(p => p.BookingRooms).HasConstraintName("booking_rooms_booking_id_fkey");

            entity.HasOne(d => d.Room).WithMany(p => p.BookingRooms)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("booking_rooms_room_id_fkey");
        });

        modelBuilder.Entity<City>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("cities_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();

            entity.HasOne(d => d.Country).WithMany(p => p.Cities)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("cities_country_id_fkey");
        });

        modelBuilder.Entity<CommercialUnit>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("commercial_units_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.Status).HasDefaultValueSql("'свободно'::character varying");

            entity.HasOne(d => d.Hotel).WithMany(p => p.CommercialUnits)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("commercial_units_hotel_id_fkey");

            entity.HasOne(d => d.UnitType).WithMany(p => p.CommercialUnits)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("commercial_units_unit_type_id_fkey");
        });

        modelBuilder.Entity<ContractUnit>(entity =>
        {
            entity.HasKey(e => new { e.ContractId, e.UnitId }).HasName("contract_units_pkey");

            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Contract).WithMany(p => p.ContractUnits).HasConstraintName("contract_units_contract_id_fkey");

            entity.HasOne(d => d.Unit).WithMany(p => p.ContractUnits)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("contract_units_unit_id_fkey");
        });

        modelBuilder.Entity<Country>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("countries_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("employees_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();

            entity.HasOne(d => d.Hotel).WithMany(p => p.Employees)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("employees_hotel_id_fkey");

            entity.HasOne(d => d.Position).WithMany(p => p.Employees)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("employees_position_id_fkey");
        });

        modelBuilder.Entity<Guest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("guests_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.FailedAttempts).HasDefaultValue(0);
            entity.Property(e => e.RegisteredAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Country).WithMany(p => p.Guests)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("guests_country_id_fkey");
        });

        modelBuilder.Entity<Hotel>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("hotels_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();

            entity.HasOne(d => d.City).WithMany(p => p.Hotels)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("hotels_city_id_fkey");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("invoices_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.IssueDate).HasDefaultValueSql("CURRENT_DATE");
            entity.Property(e => e.Status).HasDefaultValueSql("'выставлен'::character varying");

            entity.HasOne(d => d.Booking).WithMany(p => p.Invoices)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("invoices_booking_id_fkey");

            entity.HasOne(d => d.Contract).WithMany(p => p.Invoices)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("invoices_contract_id_fkey");
        });

        modelBuilder.Entity<InvoiceItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("invoice_items_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();

            entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceItems).HasConstraintName("invoice_items_invoice_id_fkey");
        });

        modelBuilder.Entity<LeaseContract>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("lease_contracts_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.Deposit).HasDefaultValueSql("0");
            entity.Property(e => e.Status).HasDefaultValueSql("'действует'::character varying");

            entity.HasOne(d => d.Tenant).WithMany(p => p.LeaseContracts)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("lease_contracts_tenant_id_fkey");
        });

        modelBuilder.Entity<LoyaltyCard>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("loyalty_cards_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.BonusPoints).HasDefaultValue(0);
            entity.Property(e => e.IssueDate).HasDefaultValueSql("CURRENT_DATE");
            entity.Property(e => e.Level).HasDefaultValueSql("'silver'::character varying");

            entity.HasOne(d => d.Guest).WithOne(p => p.LoyaltyCard).HasConstraintName("loyalty_cards_guest_id_fkey");
        });

        modelBuilder.Entity<MeterReading>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("meter_readings_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();

            entity.HasOne(d => d.Meter).WithMany(p => p.MeterReadings).HasConstraintName("meter_readings_meter_id_fkey");
        });

        modelBuilder.Entity<Payroll>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("payrolls_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();

            entity.HasOne(d => d.Employee).WithMany(p => p.Payrolls)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("payrolls_employee_id_fkey");
        });

        modelBuilder.Entity<Penalty>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("penalties_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.CalcDate).HasDefaultValueSql("CURRENT_DATE");

            entity.HasOne(d => d.Invoice).WithMany(p => p.Penalties).HasConstraintName("penalties_invoice_id_fkey");
        });

        modelBuilder.Entity<Position>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("positions_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
        });

        modelBuilder.Entity<Rate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("rates_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();

            entity.HasOne(d => d.RoomType).WithMany(p => p.Rates)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("rates_room_type_id_fkey");

            entity.HasOne(d => d.Season).WithMany(p => p.Rates)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("rates_season_id_fkey");
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("rooms_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.Status).HasDefaultValueSql("'свободен'::character varying");

            entity.HasOne(d => d.Hotel).WithMany(p => p.Rooms)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("rooms_hotel_id_fkey");

            entity.HasOne(d => d.RoomType).WithMany(p => p.Rooms)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("rooms_room_type_id_fkey");
        });

        modelBuilder.Entity<RoomType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("room_types_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
        });

        modelBuilder.Entity<Season>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("seasons_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
        });

        modelBuilder.Entity<Service>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("services_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();

            entity.HasOne(d => d.Category).WithMany(p => p.Services)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("services_category_id_fkey");

            entity.HasOne(d => d.Hotel).WithMany(p => p.Services)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("services_hotel_id_fkey");
        });

        modelBuilder.Entity<ServiceCategory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("service_categories_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
        });

        modelBuilder.Entity<ServiceOrder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("service_orders_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.OrderDate).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Booking).WithMany(p => p.ServiceOrders).HasConstraintName("service_orders_booking_id_fkey");

            entity.HasOne(d => d.Employee).WithMany(p => p.ServiceOrders)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("service_orders_employee_id_fkey");

            entity.HasOne(d => d.Service).WithMany(p => p.ServiceOrders)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("service_orders_service_id_fkey");
        });

        modelBuilder.Entity<Shift>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("shifts_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.HoursWorked).HasComputedColumnSql("round((EXTRACT(epoch FROM (shift_end - shift_start)) / (3600)::numeric), 2)", true);

            entity.HasOne(d => d.Employee).WithMany(p => p.Shifts).HasConstraintName("shifts_employee_id_fkey");
        });

        modelBuilder.Entity<Stay>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("stays_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.ActualCheckIn).HasDefaultValueSql("now()");

            entity.HasOne(d => d.BookingRoom).WithMany(p => p.Stays)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("stays_booking_room_id_fkey");

            entity.HasOne(d => d.Employee).WithMany(p => p.Stays)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("stays_employee_id_fkey");
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("tenants_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
        });

        modelBuilder.Entity<UnitType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("unit_types_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.FailedAttempts).HasDefaultValue(0);
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Employee).WithOne(p => p.User).HasConstraintName("users_employee_id_fkey");
        });

        modelBuilder.Entity<UtilityMeter>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("utility_meters_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();

            entity.HasOne(d => d.Unit).WithMany(p => p.UtilityMeters).HasConstraintName("utility_meters_unit_id_fkey");
        });

        modelBuilder.Entity<UtilityTariff>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("utility_tariffs_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
