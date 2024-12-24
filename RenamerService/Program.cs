using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;

namespace RenamerService {

    /// <summary>
    /// The main program.
    /// </summary>
    public class Program {

        /// <summary>
        /// Program's entry point.
        /// </summary>
        /// <param name="args">All command line arguments.</param>
        public static void Main(string[] args) {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddWindowsService((options) => {
                options.ServiceName = "Genshin renamer service";
            });
            LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(builder.Services);
            builder.Services.AddHostedService<GenshinRenamerService>();

            IHost host = builder.Build();
            host.Run();
        }
    }
}