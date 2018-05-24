using BlockchainObserver.Database.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BlockchainObserver.Database
{
    public class DBEntities : DbContext
    {
        public virtual DbSet<AddressCache> Addresses { get; set; }
        public virtual DbSet<TransactionCache> Transactions { get; set; }
        public virtual DbSet<BlockCache> BlockCaches { get; set; }
        public virtual DbSet<LastAddressIndex> LastAddressIndex { get; set; }

        public DBEntities(DbContextOptions<DBEntities> options) : base(options)
        {

        }
        public DBEntities() : this(new DbContextOptions<DBEntities>())
        {

        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TransactionCache>().HasIndex(t => t.Currency);

            base.OnModelCreating(modelBuilder);
            
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
                optionsBuilder.UseMySql(Startup.ConnectionString);
        }
    }
}
