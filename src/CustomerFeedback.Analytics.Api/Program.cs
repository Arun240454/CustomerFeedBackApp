using CustomerFeedback.Analytics.Api.Endpoints;
using CustomerFeedback.Analytics.Api.Services;
using CustomerFeedback.Shared.Hosting;
using CustomerFeedback.Shared.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("analytics-api");

builder.Services.Configure<AnalyticsOptions>(builder.Configuration.GetSection(AnalyticsOptions.SectionName));
builder.Services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
builder.Services.AddScoped<ISummaryService, SummaryService>();

// Asynchronous work: categorise each submission after it is stored, off the request path of the customer.
builder.Services.AddSingleton<IFeedbackCategorizer, KeywordFeedbackCategorizer>();
builder.Services.AddScoped<FeedbackCategorizationHandler>();
builder.Services.AddOutboxConsumer(builder.Configuration,
    routes => routes.Register<FeedbackSubmitted, FeedbackCategorizationHandler>());

var app = builder.Build();
app.UseServiceDefaults();
app.MapAnalyticsEndpoints();

await app.RunAsync();
