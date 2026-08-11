using JobParserTelegramBot.Configuration;
using JobParserTelegramBot.Data;
using JobParserTelegramBot.Services.Evaluation;
using JobParserTelegramBot.Services.Filtering;
using JobParserTelegramBot.Services.Telegram;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<GigaChatOptions>(builder.Configuration.GetSection(GigaChatOptions.SectionName));
builder.Services.Configure<EvaluationOptions>(builder.Configuration.GetSection(EvaluationOptions.SectionName));

builder.Services.AddSingleton<IChatRepository, JsonChatRepository>();
builder.Services.AddSingleton<IProcessedMessageStore, JsonProcessedMessageStore>();
builder.Services.AddSingleton<VacancyHeuristicFilter>();
builder.Services.AddSingleton<IVacancyEvaluationService, VacancyEvaluationService>();
builder.Services.AddSingleton<ITelegramSessionService, TelegramSessionService>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddSingleton<CommandHandler>();

builder.Services.AddHttpClient<IGigaChatClient, GigaChatClient>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

builder.Services.AddHostedService<VacancyMonitorHostedService>();

Directory.CreateDirectory("data");

var host = builder.Build();
await host.RunAsync();
