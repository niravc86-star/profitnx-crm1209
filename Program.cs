using ProfitNx.CRM.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Runtime settings are stored separately from appsettings.json so checkbox/channel
// choices survive publish/redeploy operations and can be reloaded immediately.
var runtimeSettingsDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(runtimeSettingsDirectory);
builder.Configuration.AddJsonFile("App_Data/runtime-settings.json", optional: true, reloadOnChange: true);

builder.Services.AddControllersWithViews()
    .AddMvcOptions(options =>
    {
        // FIX (2026-08-17): With <Nullable>enable</Nullable> in the csproj, ASP.NET Core
        // MVC silently treats EVERY non-nullable `string` model/view-model property as if
        // it had an implicit [Required] attribute -- both in server-side ModelState
        // validation and in the client-side data-val-required attributes emitted by
        // asp-for. That is NOT what most of these view models intend: fields like
        // ImplementationFormViewModel.PinNumber, PaymentReference, Email, BillNumber,
        // Notes, etc. are explicitly labelled "Optional" in the UI and were never given
        // an explicit [Required] attribute on purpose -- only fields that actually need
        // [Required(ErrorMessage = "...")] have it. The implicit rule was overriding
        // that intent, which is why:
        //   - PIN Number on Implementation/Training kept demanding entry even though it
        //     is optional (and is read-only once a record is created, so it could never
        //     actually be satisfied -> the popup returned on every save attempt).
        //   - "Paid training" fields (Payment Reference, etc.) were rejected by
        //     ModelState.IsValid on New Implementation / Training even when the record
        //     was never marked as paid training, since those optional fields were left
        //     empty by design in that case.
        // Suppressing the implicit behaviour restores "only what actually has
        // [Required] is required" -- which matches every explicit [Required(...)] that
        // is already in the view models today.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    });
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IGoogleSheetsService, GoogleSheetsService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IInquiryService, InquiryService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ISchemeService, SchemeService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IDealerService, DealerService>();
builder.Services.AddScoped<ICommissionService, CommissionService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IImplementationService, ImplementationService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddSingleton<ILoginSessionService, LoginSessionService>();
builder.Services.AddScoped<IInquiryDocumentService, InquiryDocumentService>();
builder.Services.AddScoped<IYearlyTargetService, YearlyTargetService>();
builder.Services.AddSingleton<IWebPushService, WebPushService>();
builder.Services.AddHostedService<ImplementationReminderWorker>();
builder.Services.AddHostedService<PendingAttentionPushWorker>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider,
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.Name;
        if (path.Equals("service-worker.js", StringComparison.OrdinalIgnoreCase)
            || path.Equals("manifest.webmanifest", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Service-Worker-Allowed"] = "/";
        }
    }
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
