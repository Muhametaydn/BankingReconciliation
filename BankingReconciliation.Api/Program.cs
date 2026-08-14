using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BankingReconciliation.Api.Data;
using BankingReconciliation.Api.Endpoints;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Security;
using BankingReconciliation.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var migrateOnly = args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
var reconciliationConnectionString = builder.Configuration.GetConnectionString("ReconciliationDatabase");
var authenticationOptions = builder.Configuration
    .GetSection(ReconciliationAuthenticationOptions.SectionName)
    .Get<ReconciliationAuthenticationOptions>() ?? new ReconciliationAuthenticationOptions();
var localAccountsEnabled = (builder.Environment.IsDevelopment() ||
        builder.Environment.IsEnvironment("Testing")) &&
    string.IsNullOrWhiteSpace(authenticationOptions.Authority);
if (localAccountsEnabled && authenticationOptions.LocalSigningKey.Length < 32)
{
    authenticationOptions.LocalSigningKey = LocalAuthenticationSigningKeyProvider.GetOrCreate(builder.Environment);
    builder.Configuration[$"{ReconciliationAuthenticationOptions.SectionName}:LocalSigningKey"] =
        authenticationOptions.LocalSigningKey;
}
var usePostgres = !builder.Environment.IsEnvironment("Testing") &&
    !string.IsNullOrWhiteSpace(reconciliationConnectionString);
var uploadOptions = builder.Configuration
    .GetSection(ReconciliationUploadOptions.SectionName)
    .Get<ReconciliationUploadOptions>() ?? new ReconciliationUploadOptions();
var immutableAuditArchiveOptions = builder.Configuration
    .GetSection(ReconciliationImmutableAuditArchiveOptions.SectionName)
    .Get<ReconciliationImmutableAuditArchiveOptions>() ??
    new ReconciliationImmutableAuditArchiveOptions();
var auditRetentionOptions = builder.Configuration
    .GetSection(ReconciliationAuditRetentionOptions.SectionName)
    .Get<ReconciliationAuditRetentionOptions>() ?? new ReconciliationAuditRetentionOptions();
var observabilityOptions = builder.Configuration
    .GetSection(ReconciliationObservabilityOptions.SectionName)
    .Get<ReconciliationObservabilityOptions>() ?? new ReconciliationObservabilityOptions();
var productionOptions = builder.Configuration
    .GetSection(ReconciliationProductionOptions.SectionName)
    .Get<ReconciliationProductionOptions>() ?? new ReconciliationProductionOptions();
var productionReadinessErrors = ReconciliationProductionReadinessValidator.Validate(
    builder.Environment.EnvironmentName,
    reconciliationConnectionString,
    builder.Configuration["AllowedHosts"],
    authenticationOptions,
    uploadOptions,
    observabilityOptions,
    productionOptions);
if (productionReadinessErrors.Count > 0)
{
    throw new InvalidOperationException(
        $"Production readiness validation failed: {string.Join(" ", productionReadinessErrors)}");
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    var localKeyPath = Path.Combine(Path.GetTempPath(), "BankingReconciliation-DataProtection");
    builder.Services
        .AddDataProtection()
        .SetApplicationName("BankingReconciliation.Local")
        .PersistKeysToFileSystem(new DirectoryInfo(localKeyPath));
}
builder.Services
    .AddOptions<ReconciliationAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(ReconciliationAuthenticationOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.OperatorRole), "OperatorRole is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApproverRole), "ApproverRole is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.PermissionClaimType), "PermissionClaimType is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApproverPermission), "ApproverPermission is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AdministratorRole), "AdministratorRole is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AdministratorPermission), "AdministratorPermission is required.")
    .Validate(options => options.ClockSkewSeconds is >= 0 and <= 300, "ClockSkewSeconds must be between 0 and 300.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.NameClaimType), "NameClaimType is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.RoleClaimType), "RoleClaimType is required.")
    .ValidateOnStart();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = authenticationOptions.RequireHttpsMetadata;
        options.IncludeErrorDetails = !ReconciliationProductionReadinessValidator.IsProductionLike(
            builder.Environment.EnvironmentName);

        if (!string.IsNullOrWhiteSpace(authenticationOptions.Authority))
        {
            options.Authority = authenticationOptions.Authority;
        }

        if (!string.IsNullOrWhiteSpace(authenticationOptions.Audience))
        {
            options.Audience = authenticationOptions.Audience;
        }

        var useLocalTokens = (builder.Environment.IsDevelopment() ||
                builder.Environment.IsEnvironment("Testing")) &&
            string.IsNullOrWhiteSpace(authenticationOptions.Authority) &&
            authenticationOptions.LocalSigningKey.Length >= 32;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = authenticationOptions.NameClaimType,
            RoleClaimType = authenticationOptions.RoleClaimType,
            ValidateIssuer = !string.IsNullOrWhiteSpace(authenticationOptions.Authority),
            ValidateAudience = !string.IsNullOrWhiteSpace(authenticationOptions.Audience),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(authenticationOptions.ClockSkewSeconds),
            IssuerSigningKey = useLocalTokens
                ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authenticationOptions.LocalSigningKey))
                : null
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ReconciliationAuthorizationPolicies.Approver, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            context.User.IsInRole(authenticationOptions.ApproverRole) ||
            context.User.HasClaim(
                authenticationOptions.PermissionClaimType,
                authenticationOptions.ApproverPermission));
    });
    options.AddPolicy(ReconciliationAuthorizationPolicies.Administrator, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            context.User.IsInRole(authenticationOptions.AdministratorRole) ||
            context.User.HasClaim(
                authenticationOptions.PermissionClaimType,
                authenticationOptions.AdministratorPermission));
    });
});
builder.Services.AddScoped<IReconciliationService>(serviceProvider => new ReconciliationService(
    Microsoft.Extensions.Options.Options.Create(
        serviceProvider.GetRequiredService<ReconciliationComparisonOptionsStore>().GetOptions())));
builder.Services.AddScoped<ITransactionFileParser>(serviceProvider => new CsvTransactionFileParser(
    Microsoft.Extensions.Options.Options.Create(
        serviceProvider.GetRequiredService<ReconciliationComparisonOptionsStore>().GetOptions()),
    Microsoft.Extensions.Options.Options.Create(
        serviceProvider.GetRequiredService<ReconciliationFileSchemaStore>().GetOptions()),
    serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ReconciliationUploadOptions>>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILocalUserAccountStore, LocalUserAccountStore>();
builder.Services.AddSingleton<LocalAuthenticationTokenService>();
if (!usePostgres)
{
    builder.Services.AddSingleton<IReconciliationHistoryRepository, InMemoryReconciliationHistoryRepository>();
    builder.Services.AddSingleton<IReconciliationSourceRepository, InMemoryReconciliationSourceRepository>();
    builder.Services.AddSingleton<IReconciliationFileSchemaRepository, InMemoryReconciliationFileSchemaRepository>();
    builder.Services.AddSingleton<IReconciliationComparisonOptionsRepository, InMemoryReconciliationComparisonOptionsRepository>();
    builder.Services.AddSingleton<IReconciliationAuditRepository, InMemoryReconciliationAuditRepository>();
}
else
{
    builder.Services.AddDbContext<ReconciliationDbContext>(options =>
        options.UseNpgsql(reconciliationConnectionString));
    builder.Services.AddScoped<IReconciliationHistoryRepository, PostgresReconciliationHistoryRepository>();
    builder.Services.AddScoped<IReconciliationSourceRepository, PostgresReconciliationSourceRepository>();
    builder.Services.AddScoped<IReconciliationFileSchemaRepository, PostgresReconciliationFileSchemaRepository>();
    builder.Services.AddScoped<IReconciliationComparisonOptionsRepository, PostgresReconciliationComparisonOptionsRepository>();
    builder.Services.AddScoped<IReconciliationAuditRepository, PostgresReconciliationAuditRepository>();
}
builder.Services.AddSingleton<IReconciliationExcelReportExporter, ReconciliationExcelReportExporter>();
builder.Services.AddSingleton<IReconciliationDatabaseSourceConfiguration, ReconciliationDatabaseSourceConfiguration>();
builder.Services.AddScoped<IReconciliationDatabaseSourceReader, PostgresReconciliationDatabaseSourceReader>();
builder.Services.AddSingleton<ReconciliationDatabaseJobQueue>();
builder.Services.AddHostedService<ReconciliationDatabaseJobWorker>();
builder.Services.AddSingleton<ReconciliationFileJobQueue>();
if (uploadOptions.TemporaryStorageMode == ReconciliationTemporaryStorageMode.S3Compatible)
{
    builder.Services.AddSingleton<IReconciliationObjectClient, S3ReconciliationObjectClient>();
    builder.Services.AddSingleton<IReconciliationTemporaryFileStore, S3ReconciliationTemporaryFileStore>();
}
else
{
    builder.Services.AddSingleton<IReconciliationTemporaryFileStore, ReconciliationTemporaryFileStore>();
}
builder.Services.AddSingleton<IReconciliationMultipartUploadReader, ReconciliationMultipartUploadReader>();
builder.Services.AddHostedService<ReconciliationFileJobWorker>();
builder.Services.AddHostedService<ReconciliationTemporaryFileCleanupService>();
builder.Services.AddSingleton<ReconciliationAuditRetentionMonitor>();
builder.Services.AddScoped<ReconciliationAuditRetentionHealthEvaluator>();
if (immutableAuditArchiveOptions.Enabled)
{
    if (immutableAuditArchiveOptions.SigningAlgorithm ==
        ReconciliationAuditSigningAlgorithm.AwsKmsRsaPssSha256)
    {
        builder.Services.AddSingleton<IReconciliationAuditArchiveSigner,
            AwsKmsReconciliationAuditArchiveSigner>();
    }
    else
    {
        builder.Services.AddSingleton<IReconciliationAuditArchiveSigner,
            LocalReconciliationAuditArchiveSigner>();
    }
    builder.Services.AddSingleton<IReconciliationImmutableAuditArchive>(serviceProvider =>
        new S3ReconciliationImmutableAuditArchive(
            serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ReconciliationImmutableAuditArchiveOptions>>(),
            serviceProvider.GetRequiredService<IReconciliationAuditArchiveSigner>()));
}
else
{
    builder.Services.AddSingleton<IReconciliationImmutableAuditArchive,
        DisabledReconciliationImmutableAuditArchive>();
}
builder.Services.AddHostedService<ReconciliationAuditRetentionService>();
if (observabilityOptions.OpenTelemetryEnabled)
{
    builder.Services
        .AddOpenTelemetry()
        .WithMetrics(metrics => metrics
            .AddMeter(ReconciliationAuditRetentionMonitor.MeterName)
            .AddOtlpExporter(options =>
                options.Endpoint = new Uri(observabilityOptions.OtlpEndpoint)));
}
builder.Services.AddSingleton<IReconciliationReadinessService, ReconciliationReadinessService>();
builder.Services
    .AddOptions<ReconciliationJobOptions>()
    .Bind(builder.Configuration.GetSection(ReconciliationJobOptions.SectionName))
    .Validate(
        options => options.LeaseDurationSeconds is >= 10 and <= 3600,
        "LeaseDurationSeconds must be between 10 and 3600.")
    .Validate(
        options => options.LeaseRenewalSeconds >= 1 &&
            options.LeaseRenewalSeconds < options.LeaseDurationSeconds,
        "LeaseRenewalSeconds must be positive and shorter than LeaseDurationSeconds.")
    .Validate(
        options => options.PollIntervalMilliseconds is >= 100 and <= 60_000,
        "PollIntervalMilliseconds must be between 100 and 60000.")
    .Validate(
        options => options.MaxAttempts is >= 1 and <= 20,
        "MaxAttempts must be between 1 and 20.")
    .Validate(
        options => options.RetryDelaySeconds is >= 0 and <= 3600,
        "RetryDelaySeconds must be between 0 and 3600.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ReconciliationAuditRetentionOptions>()
    .Bind(builder.Configuration.GetSection(ReconciliationAuditRetentionOptions.SectionName))
    .Validate(
        ReconciliationAuditRetentionOptionsValidator.IsValid,
        "Audit retention requires valid hot/archive periods, interval, and batch size; archive retention must be longer than hot retention.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ReconciliationObservabilityOptions>()
    .Bind(builder.Configuration.GetSection(ReconciliationObservabilityOptions.SectionName))
    .Validate(
        ReconciliationObservabilityOptionsValidator.IsValid,
        "OpenTelemetry requires an absolute HTTP or HTTPS OTLP endpoint when enabled.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ReconciliationImmutableAuditArchiveOptions>()
    .Bind(builder.Configuration.GetSection(ReconciliationImmutableAuditArchiveOptions.SectionName))
    .Validate(
        ReconciliationImmutableAuditArchiveOptionsValidator.IsValid,
        "Immutable audit archive requires valid S3/Object Lock settings and either a base64 HMAC key of at least 32 bytes or a matching RSA-PSS private/public key pair of at least 2048 bits.")
    .Validate(
        options => ReconciliationImmutableAuditArchiveOptionsValidator.IsRetentionCompatible(
            options,
            auditRetentionOptions),
        "Object Lock retention must be at least as long as the PostgreSQL audit archive retention.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ReconciliationReadinessOptions>()
    .Bind(builder.Configuration.GetSection(ReconciliationReadinessOptions.SectionName))
    .Validate(
        options => options.TimeoutSeconds is >= 1 and <= 60,
        "Readiness TimeoutSeconds must be between 1 and 60.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ReconciliationUploadOptions>()
    .Bind(builder.Configuration.GetSection(ReconciliationUploadOptions.SectionName))
    .Validate(options => options.MaxCsvFileSizeBytes > 0, "MaxCsvFileSizeBytes must be greater than 0.")
    .Validate(
        options => options.SynchronousComparisonMaxFileSizeBytes > 0,
        "SynchronousComparisonMaxFileSizeBytes must be greater than 0.")
    .Validate(options => options.MaxRecordsPerFile > 0, "MaxRecordsPerFile must be greater than 0.")
    .Validate(
        options => options.BackgroundQueueCapacity is >= 1 and <= 10_000,
        "BackgroundQueueCapacity must be between 1 and 10000.")
    .Validate(
        ReconciliationUploadOptionsValidator.HasValidTemporaryStorage,
        "Temporary storage must be Local, an absolute SharedFileSystem path, or a valid S3-compatible bucket/prefix/region configuration.")
    .Validate(
        options => options.TemporaryFileRetentionHours is >= 1 and <= 24 * 365,
        "TemporaryFileRetentionHours must be between 1 and 8760.")
    .Validate(
        options => options.TemporaryFileCleanupIntervalMinutes is >= 1 and <= 24 * 60,
        "TemporaryFileCleanupIntervalMinutes must be between 1 and 1440.")
    .Validate(
        options => options.TemporaryFileCleanupBatchSize is >= 1 and <= 10_000,
        "TemporaryFileCleanupBatchSize must be between 1 and 10000.")
    .Validate(
        options => options.AllowedFileExtensions.Length > 0 &&
            options.AllowedFileExtensions.All(extension => extension.StartsWith('.')),
        "AllowedFileExtensions must contain file extensions that start with '.'.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ReconciliationDatabaseSourcesOptions>()
    .Bind(builder.Configuration.GetSection(ReconciliationDatabaseSourcesOptions.SectionName))
    .Validate(
        ReconciliationDatabaseSourcesOptionsValidator.IsValid,
        "Database source definitions must use unique BRANCH/BANK codes, named connection strings, read-only queries, a positive record limit, and a timeout between 1 and 300 seconds.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ReconciliationComparisonOptions>()
    .Bind(builder.Configuration.GetSection(ReconciliationComparisonOptions.SectionName))
    .Validate(
        ReconciliationComparisonOptionsValidator.HasValidDecimalPlaces,
        "Decimal place settings must be between 0 and 10.")
    .Validate(
        ReconciliationComparisonOptionsValidator.HasValidTolerances,
        "Quantity and amount tolerances must be zero or greater.")
    .Validate(
        ReconciliationComparisonOptionsValidator.HasValidMatchingFields,
        "MatchingFields must contain unique supported transaction fields.")
    .Validate(
        ReconciliationComparisonOptionsValidator.HasValidComparisonFields,
        "ComparisonFields must contain unique supported comparison fields.")
    .Validate(
        ReconciliationComparisonOptionsValidator.HasValidResultFields,
        "ResultFields must contain unique supported transaction fields.")
    .Validate(
        ReconciliationComparisonOptionsValidator.HasValidMappings,
        "Mappings must contain non-empty source and target values.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ReconciliationFileSchemaOptions>()
    .Bind(builder.Configuration.GetSection(ReconciliationFileSchemaOptions.SectionName))
    .Validate(
        ReconciliationFileSchemaOptionsValidator.HasRequiredTransactionFields,
        "File schema must contain each required transaction field exactly once.")
    .Validate(
        ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions,
        "File schema columns must have valid names, types, and date formats.")
    .Validate(
        ReconciliationFileSchemaOptionsValidator.HasUniqueColumnNames,
        "File schema column names must be unique.")
    .Validate(
        ReconciliationFileSchemaOptionsValidator.HasUniqueFieldNames,
        "File schema field names must be unique.")
    .ValidateOnStart();
builder.Services.AddSingleton<ReconciliationFileSchemaStore>();
builder.Services.AddSingleton<ReconciliationComparisonOptionsStore>();
builder.Services
    .AddOptions<ReconciliationProductionOptions>()
    .Bind(builder.Configuration.GetSection(ReconciliationProductionOptions.SectionName))
    .Validate(
        ReconciliationProductionReadinessValidator.HasValidRuntimeOptions,
        "Production rate limits and trusted proxy networks are invalid.")
    .ValidateOnStart();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = productionOptions.RateLimitPermitCount,
                Window = TimeSpan.FromSeconds(productionOptions.RateLimitWindowSeconds),
                QueueLimit = productionOptions.RateLimitQueueCount,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                error = "RateLimitExceeded",
                message = "Too many requests. Try again later."
            },
            cancellationToken);
    };
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

if (usePostgres)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ReconciliationDbContext>();
    if (migrateOnly || productionOptions.ApplyDatabaseMigrationsOnStartup)
    {
        dbContext.Database.Migrate();
    }
    else
    {
        var pendingMigrations = dbContext.Database.GetPendingMigrations().ToArray();
        if (pendingMigrations.Length > 0)
        {
            throw new InvalidOperationException(
                "Database migrations are pending. Run the deployment migration job before starting the application.");
        }
    }

    try
    {
        var persistedSchema = scope.ServiceProvider
            .GetRequiredService<IReconciliationFileSchemaRepository>()
            .Get();
        if (persistedSchema is not null && IsValidFileSchema(persistedSchema))
        {
            app.Services.GetRequiredService<ReconciliationFileSchemaStore>().Update(persistedSchema);
        }
        else if (persistedSchema is not null)
        {
            app.Logger.LogWarning("Persisted reconciliation file schema is invalid. Configuration schema will be used.");
        }

        var persistedComparisonOptions = scope.ServiceProvider
            .GetRequiredService<IReconciliationComparisonOptionsRepository>()
            .Get();
        if (persistedComparisonOptions is not null && IsValidComparisonOptions(
                persistedComparisonOptions,
                app.Services.GetRequiredService<ReconciliationFileSchemaStore>().GetOptions()))
        {
            app.Services.GetRequiredService<ReconciliationComparisonOptionsStore>()
                .Update(persistedComparisonOptions);
        }
        else if (persistedComparisonOptions is not null)
        {
            app.Logger.LogWarning("Persisted reconciliation comparison settings are invalid. Configuration settings will be used.");
        }
    }
    catch (Exception exception)
    {
        app.Logger.LogWarning(
            exception,
            "Persisted reconciliation settings could not be loaded. Configuration settings will be used.");
    }
}

if (migrateOnly)
{
    app.Logger.LogInformation("Database migrations completed successfully.");
    return;
}

if (ReconciliationProductionReadinessValidator.IsProductionLike(app.Environment.EnvironmentName))
{
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1
    };
    forwardedHeadersOptions.KnownNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    foreach (var network in productionOptions.KnownProxyNetworks)
    {
        forwardedHeadersOptions.KnownNetworks.Add(ParseKnownNetwork(network));
    }
    app.UseForwardedHeaders(forwardedHeadersOptions);
    app.UseHsts();
}

if (ReconciliationProductionReadinessValidator.IsProductionLike(app.Environment.EnvironmentName))
{
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    var contentSecurityPolicy = app.Environment.IsDevelopment() ||
        app.Environment.IsEnvironment("Testing")
            ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'"
            : "default-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers.Append("Content-Security-Policy", contentSecurityPolicy);
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
if (localAccountsEnabled)
{
    app.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var actor = ReconciliationUserIdentity.GetActor(context.User);
            var userStore = context.RequestServices.GetRequiredService<ILocalUserAccountStore>();
            var localUser = actor is null ? null : userStore.GetByUsername(actor);
            if (localUser is not null)
            {
                var tokenService = context.RequestServices.GetRequiredService<LocalAuthenticationTokenService>();
                var expectedRole = tokenService.GetConfiguredRole(localUser.Role);
                if (!context.User.IsInRole(expectedRole))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
            }
        }

        await next();
    });
}
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new
{
    Application = "Banking Reconciliation API",
    Status = "Running"
}));
app.MapGet(
    "/api/health/ready",
    async (
        IReconciliationReadinessService readinessService,
        CancellationToken cancellationToken) =>
    {
        var readiness = await readinessService.CheckAsync(cancellationToken);
        return Results.Json(
            new
            {
                Application = "Banking Reconciliation API",
                Status = readiness.IsReady ? "Ready" : "NotReady",
                Checks = new
                {
                    Database = readiness.DatabaseAvailable ? "Ready" : "Unavailable",
                    TemporaryStorage = readiness.TemporaryStorageAvailable
                        ? "Ready"
                        : "Unavailable"
                }
            },
            statusCode: readiness.IsReady
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable);
    });

app.MapReconciliationEndpoints();
app.MapReconciliationSourceEndpoints();
app.MapReconciliationAuditEndpoints();
app.MapLocalAuthenticationEndpoints(app.Environment);

app.Run();

static bool IsValidFileSchema(ReconciliationFileSchemaOptions options)
{
    return ReconciliationFileSchemaOptionsValidator.HasRequiredTransactionFields(options) &&
        ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options) &&
        ReconciliationFileSchemaOptionsValidator.HasUniqueColumnNames(options) &&
        ReconciliationFileSchemaOptionsValidator.HasUniqueFieldNames(options);
}

static bool IsValidComparisonOptions(
    ReconciliationComparisonOptions options,
    ReconciliationFileSchemaOptions schemaOptions)
{
    return ReconciliationComparisonOptionsValidator.HasValidDecimalPlaces(options) &&
        ReconciliationComparisonOptionsValidator.HasValidMatchingFields(options) &&
        ReconciliationComparisonOptionsValidator.HasValidComparisonFields(options) &&
        ReconciliationComparisonOptionsValidator.HasValidResultFields(options) &&
        ReconciliationComparisonOptionsValidator.HasValidMappings(options) &&
        ReconciliationComparisonOptionsValidator.HasFieldsCompatibleWithSchema(options, schemaOptions);
}

static Microsoft.AspNetCore.HttpOverrides.IPNetwork ParseKnownNetwork(string value)
{
    var parts = value.Split('/', StringSplitOptions.TrimEntries);
    return new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
        System.Net.IPAddress.Parse(parts[0]),
        int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
}

public partial class Program;
