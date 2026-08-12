
using AsynCUDA13.Media;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace AsynCUDA13.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Setup StaticLogger with appsettings
            string? logDirectory = builder.Configuration.GetValue<string>("LogDirectory");
            bool createLogFile = builder.Configuration.GetValue<bool>("CreateLogFile");
            int maxLogFiles = builder.Configuration.GetValue<int>("MaxLogFiles");
            StaticLogger.InitializeLogFiles(logDirectory, createLogFile, maxLogFiles);
            StaticLogger.SetUiContext(SynchronizationContext.Current ?? new SynchronizationContext());

            // Add services to the container.
            builder.Services.AddSingleton<ICudaService, CudaService>();
            builder.Services.AddSingleton<AudioCollection>();
            builder.Services.AddSingleton<ImageCollection>();

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new() { Title = "AsynCUDA13.API v1", Version = "v1" });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(options =>
                {
                    options.SwaggerEndpoint("/swagger/v1/swagger.json", "AsynCUDA13.API v1");
                    options.RoutePrefix = "swagger";
                });
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
