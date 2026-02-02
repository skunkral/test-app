using Viana.Core.Interfaces;
using Viana.Infrastructure.FileSystem;
using Viana.Infrastructure.Services;
using Viana.Sentinel.Service;

var builder = Host.CreateApplicationBuilder(args);

// Register Infrastructure Services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IDownloader, ResumableDownloader>();
builder.Services.AddSingleton<IUpdateManager, ManifestManager>();
builder.Services.AddSingleton<RollbackManager>();

// Register Worker Service
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
