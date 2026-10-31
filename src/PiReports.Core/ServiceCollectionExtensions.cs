using Microsoft.Extensions.DependencyInjection;
using PiReports.Core.Capture;
using PiReports.Core.Delivery;
using PiReports.Core.Jobs;
using PiReports.Core.Transfer;

namespace PiReports.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPiReportsCore(this IServiceCollection services)
    {
        services.AddSingleton<IScreenCapturer, PuppeteerScreenCapturer>();
        services.AddScoped<EmailSender>();
        services.AddScoped<CaptureScheduler>();
        services.AddScoped<CaptureJob>();
        services.AddScoped<RetentionJob>();
        services.AddScoped<CaptureTransfer>();
        return services;
    }
}
