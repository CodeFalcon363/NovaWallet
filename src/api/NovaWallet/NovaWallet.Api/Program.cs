using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Data;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var connectionString = builder.Configuration.GetConnectionString("NovaWalletDb")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:NovaWalletDb configuration.");

            builder.Services.AddDbContext<NovaWalletDbContext>(options => options.UseSqlServer(connectionString));
            builder.Services.AddSingleton<ISqlConnectionFactory>(new SqlConnectionFactory(connectionString));

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<NovaWalletDbContext>().Database.Migrate();
            }

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
