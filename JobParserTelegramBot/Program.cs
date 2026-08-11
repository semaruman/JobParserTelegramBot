using System.Runtime.InteropServices;
using JobParserTelegramBot.Configuration;
using JobParserTelegramBot.Data;
using JobParserTelegramBot.Services.Evaluation;
using JobParserTelegramBot.Services.Filtering;
using JobParserTelegramBot.Services.Telegram;
using JobParserTelegramBot.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobParserTelegramBot;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [STAThread]
    private static async Task Main(string[] args)
    {
        // Console for Telegram login code / logs when running as WinExe
        AllocConsole();
        ApplicationConfiguration.Initialize();

        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
        builder.Services.Configure<GigaChatOptions>(builder.Configuration.GetSection(GigaChatOptions.SectionName));
        builder.Services.Configure<EvaluationOptions>(builder.Configuration.GetSection(EvaluationOptions.SectionName));

        builder.Services.AddSingleton<IChatRepository, JsonChatRepository>();
        builder.Services.AddSingleton<IProcessedMessageStore, JsonProcessedMessageStore>();
        builder.Services.AddSingleton<VacancyHeuristicFilter>();
        builder.Services.AddSingleton<IVacancyEvaluationService, VacancyEvaluationService>();
        builder.Services.AddSingleton<ITelegramSessionService, TelegramSessionService>();
        builder.Services.AddSingleton<IChatManagementService, ChatManagementService>();
        builder.Services.AddSingleton<INotificationService, NotificationService>();
        builder.Services.AddSingleton<CommandHandler>();
        builder.Services.AddSingleton<MainForm>();

        builder.Services.AddHttpClient<IGigaChatClient, GigaChatClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });

        builder.Services.AddHostedService<VacancyMonitorHostedService>();

        Directory.CreateDirectory("data");

        var host = builder.Build();
        await host.StartAsync();

        try
        {
            var form = host.Services.GetRequiredService<MainForm>();
            Application.Run(form);
        }
        finally
        {
            await host.StopAsync(TimeSpan.FromSeconds(10));
            host.Dispose();
        }
    }
}
