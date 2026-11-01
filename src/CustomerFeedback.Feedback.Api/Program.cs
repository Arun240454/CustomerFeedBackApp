using CustomerFeedback.Feedback.Api.Contracts;
using CustomerFeedback.Feedback.Api.Endpoints;
using CustomerFeedback.Feedback.Api.Services;
using CustomerFeedback.Shared.Hosting;
using CustomerFeedback.Shared.Messaging;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("feedback-api");

// This service only publishes events (transactional outbox); consumers live in other services.
builder.Services.AddOutboxPublisher();
builder.Services.AddScoped<IValidator<SubmitFeedbackRequest>, SubmitFeedbackRequestValidator>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();

var app = builder.Build();
app.UseServiceDefaults();
app.MapFeedbackEndpoints();

await app.RunAsync();
