using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Minimal sample. Will demonstrate AddGeodeClient + IGeodeCache injection
// once Phase 5 (DI wiring) lands.
var builder = Host.CreateApplicationBuilder(args);

// builder.Services.AddGeodeClient(builder.Configuration.GetSection("Geode"));

using var host = builder.Build();
host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Sample")
    .LogInformation("Geode .NET Client sample - skeleton phase. See CLAUDE.md.");

await host.RunAsync();
