using Azure.Identity;
using Azure.ResourceManager;
using job_seeker.Server.Authorization;
using job_seeker.Server.Operations.Email;
using job_seeker.Server.Operations.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Azure;
using Resend;

var builder = WebApplication.CreateBuilder(args);

// Gitignored, local-only overrides (e.g. real AZURE_SUBSCRIPTION_ID/AZURE_RESOURCE_GROUP
// for testing the scrape trigger against a real Container App Job from a dev machine).
// Not loaded automatically by ASP.NET Core — must be wired up explicitly.
builder.Configuration.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var credential = new DefaultAzureCredential();

// KEY_VAULT_URI is a non-secret app setting injected by webapp.bicep
var keyVaultUri = builder.Configuration["KEY_VAULT_URI"] ?? "https://job-seeker-kv-dev.vault.azure.net/";
if (!string.IsNullOrEmpty(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), credential);
}

// AZURE_TABLE_SERVICE_ENDPOINT / AZURE_BLOB_SERVICE_ENDPOINT are non-secret app settings
// injected by webapp.bicep. Locally we fall back to storage-connection-string.
builder.Services.AddAzureClients(b =>
{
    var tableEndpoint = builder.Configuration["AZURE_TABLE_SERVICE_ENDPOINT"];
    var blobEndpoint = builder.Configuration["AZURE_BLOB_SERVICE_ENDPOINT"];
    if (!string.IsNullOrEmpty(tableEndpoint))
    {
        b.AddTableServiceClient(new Uri(tableEndpoint));
        b.AddBlobServiceClient(new Uri(blobEndpoint!));
        b.UseCredential(credential);
    }
    else
    {
        var connectionString = builder.Configuration["storage-connection-string"];
        b.AddTableServiceClient(connectionString);
        b.AddBlobServiceClient(connectionString);
    }
});

builder.Services.AddAuthorization();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.RequireHttpsMetadata = false;
        o.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(builder.Configuration["jwt-secret"] ?? "dev-secret-change-me")),
            ValidIssuer = builder.Configuration["jwt-issuer"],
            ValidAudience = builder.Configuration["jwt-audience"],
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddOptions();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(o =>
{
    o.ApiToken = builder.Configuration["resend-api-key"] ?? string.Empty;
});
builder.Services.AddTransient<IResend, ResendClient>();

builder.Services.AddSingleton<TokenHandler>();
builder.Services.AddSingleton<ICustomerStorage, CustomerTableStorage>();
builder.Services.AddSingleton<ICriteriaStorage, CriteriaTableStorage>();
builder.Services.AddSingleton<ICriteriaBlobStorage, CriteriaBlobStorage>();
builder.Services.AddSingleton<IJobLinkStorage, JobLinkTableStorage>();
builder.Services.AddSingleton<IJobResultStorage, JobResultBlobStorage>();
builder.Services.AddSingleton<IResumeStorage, ResumeTableStorage>();
builder.Services.AddSingleton<IResumeBlobStorage, ResumeBlobStorage>();
builder.Services.AddSingleton(new ArmClient(credential));
builder.Services.AddScoped<ISendEmail, SendEmail>();

// APPLICATIONINSIGHTS_CONNECTION_STRING is an App Service app setting — not available locally
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("/index.html");

app.Run();
