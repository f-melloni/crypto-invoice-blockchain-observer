using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BlockchainObserver.Utils;
using Microsoft.EntityFrameworkCore;
using BlockchainObserver.Database;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace BlockchainObserver
{
    public class Startup
    {
        public static IConfiguration Configuration { get; set; }
        public static string ConnectionString { get; set; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            ConnectionString = Configuration.GetConnectionString("DefaultConnection");
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<DBEntities>(options => options.UseMySql(Configuration.GetConnectionString("DefaultConnection"), b => b.MigrationsAssembly("BlockchainObserver")));
            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IConfiguration configuration, DBEntities dBEntities)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseMvc();

            // Check if Starup is invoked by entityFramework and if so we can't continue because of infinite loop in Observer
            StackTrace stackTrace = new StackTrace();
            List<string> efMethods = new List<string>() { "RemoveMigration", "AddMigration", "UpdateDatabase" };
            if (stackTrace.GetFrames().Any(f => efMethods.Contains(f.GetMethod().Name))) {
                return;
            }
            
            RabbitMessenger.Setup(configuration);
            Observer.Setup(configuration);
            NBitcoin.Litecoin.Networks.Register();
        }
    }
}
