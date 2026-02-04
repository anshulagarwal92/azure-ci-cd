using DemoApplication.Models;
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
}