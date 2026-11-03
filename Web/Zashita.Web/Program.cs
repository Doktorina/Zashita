using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Zashita.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 25 * 1024 * 1024;
});

// Большие справочники ЕТН: много полей в форме + загрузка .xlsx
builder.Services.Configure<FormOptions>(options =>
{
    options.ValueCountLimit = 50_000;
    options.ValueLengthLimit = 1024 * 1024;
    options.MultipartBodyLengthLimit = 25 * 1024 * 1024;
    options.MultipartHeadersLengthLimit = 64 * 1024;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.Configure<Zashita.Web.Services.AuthOptions>(
    builder.Configuration.GetSection(Zashita.Web.Services.AuthOptions.SectionName));
builder.Services.Configure<OneCOptions>(
    builder.Configuration.GetSection(OneCOptions.SectionName));
builder.Services.Configure<WokUsersOptions>(
    builder.Configuration.GetSection(WokUsersOptions.SectionName));
builder.Services.AddHttpClient("wok", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddSingleton<WokDbTunnelService>();
builder.Services.AddSingleton<WokUsersService>();
builder.Services.AddSingleton<Zashita.Web.Services.SiteAccessStore>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
    [
        "application/javascript",
        "text/css",
        "application/json",
        "image/svg+xml"
    ]);
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Fastest;
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Fastest;
});
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddSingleton(new Zashita.Core.Ppe.PpeDataPaths(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddSingleton(new Zashita.Core.Ppe.Appendix1NormsStore(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddSingleton<Zashita.Core.Ppe.IssueStandardsStore>();
builder.Services.AddSingleton<Zashita.Core.Ppe.SavedSiosCardsStore>();
builder.Services.AddSingleton<Zashita.Core.Ppe.EtnNomenclatureStore>();
builder.Services.AddSingleton<Zashita.Core.Ppe.PollutantsEtnStore>();
builder.Services.AddSingleton<Zashita.Core.Ppe.PpeNomenclatureStore>();
builder.Services.AddSingleton<Zashita.Core.Ppe.PpeChangesStore>();
builder.Services.AddSingleton<Zashita.Core.Ppe.PpeWriteoffStore>();
builder.Services.AddSingleton<Zashita.Core.Ppe.PpeAcceptanceStore>();
builder.Services.AddSingleton<Zashita.Core.Ppe.PpeProvisionStore>();
builder.Services.AddSingleton<Zashita.Core.Ppe.PpeNormsRegisterPrintOverridesStore>();
builder.Services.AddSingleton<Zashita.Core.Ppe.PpeReferenceCatalogStore>();
builder.Services.AddSingleton<Zashita.Core.Ppe.PpeInternalOrdersStore>();
builder.Services.AddSingleton<Zashita.Web.Services.OrganizationEmployeesStore>();
builder.Services.AddSingleton<Zashita.Web.Services.PpeEmployeesService>();
builder.Services.AddSingleton<Zashita.Web.Services.OrganizationEmployeesService>();
builder.Services.AddSingleton<OneCSyncLogStore>();
builder.Services.AddSingleton(new Zashita.Core.Employees.StaffingChangeStore(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddSingleton<Zashita.Web.Services.StaffingChangeResetService>();
builder.Services.AddSingleton<Zashita.Web.Services.StaffingOnboardingQuery>();
builder.Services.AddSingleton<OneCEmployeeSyncService>();
builder.Services.AddSingleton<Zashita.Web.Services.DismissedEmployeePurgeService>();
builder.Services.AddSingleton<Zashita.Web.Services.EmployeeDuplicateCleanupService>();
builder.Services.AddHostedService<OneCDailySyncHostedService>();
builder.Services.AddSingleton<Zashita.Web.Services.AppDataBackupService>();
builder.Services.AddHostedService<Zashita.Web.Services.AppDataHourlyBackupHostedService>();
builder.Services.AddSingleton(new Zashita.Core.Training.TrainingDataPaths(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddSingleton<Zashita.Core.Training.TrainingStandardsStore>();
builder.Services.AddSingleton<Zashita.Core.Training.TrainingPersonStandardAssignmentStore>();
builder.Services.AddSingleton<Zashita.Core.Training.TrainingStandardHistoryStore>();
builder.Services.AddSingleton<Zashita.Core.Training.TrainingConductStore>();
builder.Services.AddSingleton<Zashita.Core.Training.TrainingScheduleImportService>();
builder.Services.AddSingleton<Zashita.Core.Training.TrainingAnnualPlanStore>();
builder.Services.AddSingleton<Zashita.Core.Training.TrainingPlanTypeStore>();
builder.Services.AddSingleton<Zashita.Core.Training.TrainingMappingStore>();
builder.Services.AddSingleton<Zashita.Core.Training.TrainingPlanningStore>();
builder.Services.AddSingleton<Zashita.Web.Services.TrainingApplicationEditor>();
builder.Services.AddSingleton<Zashita.Core.Training.TrainingKindStore>();
builder.Services.AddSingleton<Zashita.Core.Training.TrainingProgramStore>();
builder.Services.AddSingleton<Zashita.Core.Training.TrainingSimpleTreeStore>();
builder.Services.AddSingleton<Zashita.Core.Training.Journals.TrainingJournalStore>();
builder.Services.AddSingleton<Zashita.Core.Training.Journals.TrainingJournalHistoryStore>();
builder.Services.AddSingleton(new Zashita.Core.PrintHistory.DocumentPrintHistoryStore(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddSingleton<Zashita.Core.Training.Journals.TrainingPrintHistoryStore>(sp =>
    new Zashita.Core.Training.Journals.TrainingPrintHistoryStore(
        sp.GetRequiredService<Zashita.Core.Training.TrainingDataPaths>(),
        sp.GetRequiredService<Zashita.Core.PrintHistory.DocumentPrintHistoryStore>()));
builder.Services.AddSingleton<Zashita.Core.Training.Journals.TrainingJournalEntryBuilder>(sp =>
{
    var orgEmployees = sp.GetRequiredService<Zashita.Web.Services.OrganizationEmployeesService>();
    return new Zashita.Core.Training.Journals.TrainingJournalEntryBuilder(
        orgEmployees.GetByProfession,
        org =>
        {
            var map = new Dictionary<string, Zashita.Core.Training.Journals.TrainingJournalPersonInfo>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var e in orgEmployees.Load(org))
            {
                if (string.IsNullOrWhiteSpace(e.Name))
                {
                    continue;
                }

                var key = e.Name.Trim();
                map[key] = new Zashita.Core.Training.Journals.TrainingJournalPersonInfo(
                    key,
                    e.Profession?.Trim() ?? string.Empty,
                    e.Department?.Trim() ?? string.Empty,
                    e.BirthDate);
            }

            return map;
        });
});
builder.Services.AddSingleton(sp =>
    new Zashita.Core.Training.Journals.TrainingJournalExportService(
        Path.Combine(sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "Templates")));
builder.Services.AddSingleton(new Zashita.Core.Med.MedDataPaths(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddSingleton<Zashita.Core.Med.MedExamStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedConclusionScanStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedConclusionDocxImportService>();
// OCR сканов заключений (PDF-картинка) → дата выдачи / факторы / Годен.
// Windows: встроенный OCR; Linux-сервер: tesseract (если установлен).
Zashita.Core.Med.MedConclusionPdfImportParser.ImageOcrFallback = images =>
{
    var win = Zashita.Web.Services.MedConclusionWindowsOcr.TryRecognizeImages(images);
    if (!string.IsNullOrWhiteSpace(win))
    {
        return win;
    }

    return Zashita.Web.Services.MedConclusionTesseractOcr.TryRecognizeImages(images);
};
builder.Services.AddSingleton<Zashita.Core.Med.MedPersonListStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedRestrictionStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedPhysicalRestrictionNotesStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedScheduleStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedFactorStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedWorkTypeStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedContingentOverridesStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedRegistryStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedReferenceStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedStandardStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedDirectionStore>();
builder.Services.AddSingleton<Zashita.Core.Med.MedicalOrganizationStore>();
builder.Services.AddSingleton<Zashita.Web.Services.MedDirectionSignatureStorage>();
builder.Services.AddSingleton<Zashita.Web.Services.MedContextService>();
builder.Services.AddSingleton<Zashita.Web.Services.HomeReportSummaryService>();
builder.Services.AddSingleton<Zashita.Web.Services.HomeDeadlinesService>();
builder.Services.AddSingleton(new Zashita.Core.Suot.SuotDataPaths(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddSingleton<Zashita.Core.Suot.SuotIndicatorStore>();
builder.Services.AddSingleton<Zashita.Core.Suot.SuotIndicatorFactStore>();
builder.Services.AddSingleton(new Zashita.Core.Incidents.IncidentDataPaths(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddSingleton<Zashita.Core.Incidents.WorkplaceAccidentStore>();
builder.Services.AddSingleton<Zashita.Core.Incidents.AccidentInvestigationStore>();
builder.Services.AddSingleton<Zashita.Core.Incidents.MicrotraumaStore>();
builder.Services.AddSingleton<Zashita.Core.Incidents.MicrotraumaInvestigationStore>();
builder.Services.AddSingleton<Zashita.Core.Incidents.IncidentCatalogStore>();
builder.Services.AddSingleton(new Zashita.Core.Risks.RiskDataPaths(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddSingleton<Zashita.Core.Risks.WorkplaceStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.RiskWorkplaceStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.WorkplaceAssignmentStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.SoutPreparationStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.SoutAnnualPlanStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.SoutCatalogStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.SoutConditionsClassStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.SoutCommissionStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.RiskPreparationStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.RiskUnscheduledOrderStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.RiskHazardAssessmentStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.RiskHazardCatalogStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.RiskHazardPrintSelectionStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.RiskAssessmentObjectStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.OrganizationPrintDetailsStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.RiskRegulationTemplateStore>();
builder.Services.AddSingleton<Zashita.Web.Services.OrganizationRequisitesStore>();
builder.Services.AddSingleton<Zashita.Core.Risks.SoutXtmImportService>();
builder.Services.AddSingleton<Zashita.Core.Risks.RiskCardDocxImportService>();
builder.Services.AddSingleton<Zashita.Core.Risks.SoutCardDocxImportService>();
builder.Services.AddSingleton(new Zashita.Core.Compliance.ComplianceDataPaths(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddSingleton<Zashita.Core.Compliance.InspectionActStore>();
builder.Services.AddSingleton(new Zashita.Core.Electrical.ElectricalDataPaths(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddSingleton<Zashita.Core.Electrical.ElectricalKnowledgeCheckStore>();
builder.Services.AddSingleton<Zashita.Core.Electrical.ElectricalCommissionStore>();
builder.Services.AddSingleton<Zashita.Core.Excel.ExcelWorkbookStore>();
builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var configured = builder.Configuration["KristinaProfessionsPath"];
    return new Zashita.Core.Employees.KristinaProfessionsStore(
        Path.Combine(env.ContentRootPath, "App_Data"),
        configured);
});
builder.Services.AddSingleton<Zashita.Web.Services.HtmlPrintService>();
builder.Services.AddSingleton<Zashita.Web.Services.OrganizationStore>();
builder.Services.AddSingleton<Zashita.Web.Services.RisksWorkspaceContext>();
builder.Services.AddSingleton<Zashita.Web.Services.OrganizationRenameService>();
builder.Services.AddSingleton<Zashita.Web.Services.MedDataOrganizationAligner>();
builder.Services.AddSingleton<Zashita.Web.Services.MedExamRosterAligner>();
builder.Services.AddSingleton<Zashita.Web.Services.MedStatusAuditService>();
builder.Services.AddSingleton<Zashita.Web.Services.GlobalSearchService>();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    options.Conventions.ConfigureFilter(new Zashita.Web.Filters.RisksModulePageFilter());
})
.AddMvcOptions(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});

var app = builder.Build();

try
{
    var workersFile = Path.Combine(app.Environment.ContentRootPath, "App_Data", "workers.xlsx");
    if (File.Exists(workersFile))
    {
        app.Services.GetRequiredService<Zashita.Core.Excel.ExcelWorkbookStore>().LoadFromFile(workersFile);
    }
}
catch
{
    // ignore broken workbook on startup
}

app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(() =>
    {
        try
        {
            RunStartupMaintenance(app.Services, app.Environment.ContentRootPath);
        }
        catch
        {
            // ignore org alignment failures on startup
        }
    });
});

if (args.Any(a => string.Equals(a, "--audit-med", StringComparison.OrdinalIgnoreCase)))
{
    var medAudit = app.Services.GetRequiredService<Zashita.Web.Services.MedStatusAuditService>();
    var report = medAudit.RunFullAudit(DateTime.Today);
    MedStatusAuditLog.Print(report);
    return;
}

if (args.Any(a => string.Equals(a, "--sync-1c", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(a, "--rebuild-staff", StringComparison.OrdinalIgnoreCase)))
{
    var sync = app.Services.GetRequiredService<OneCEmployeeSyncService>();
    var source = args.Any(a => string.Equals(a, "--rebuild-staff", StringComparison.OrdinalIgnoreCase))
        ? "cli-rebuild-staff"
        : "cli-sync-1c";
    // Полная перезапись organization-employees.json из CSV 1С (без «призраков» старого стора).
    var run = sync.ReplaceFromShared(source);
    Console.WriteLine(run.Success ? "OK" : "FAILED");
    Console.WriteLine(run.Message);
    if (run.Errors.Count > 0)
    {
        foreach (var error in run.Errors)
        {
            Console.WriteLine("  " + error);
        }
    }

    // Тяжёлую зачистку дублей (--cleanup-duplicates) не запускаем здесь:
    // она может висеть десятки минут и рвать SSH/cron. Уволенные из медсписков
    // уже вычищаются внутри ReplaceFromShared.
    if (args.Any(a => string.Equals(a, "--with-cleanup", StringComparison.OrdinalIgnoreCase)))
    {
        var duplicateCleanup = app.Services.GetRequiredService<Zashita.Web.Services.EmployeeDuplicateCleanupService>();
        var cleanup = duplicateCleanup.RunFullCleanup();
        Console.WriteLine(
            $"Дублей убрано: {cleanup.TotalRemoved}, выровнено медосмотров: {cleanup.AlignedExams}");
    }

    return;
}

if (args.Any(a => string.Equals(a, "--cleanup-duplicates", StringComparison.OrdinalIgnoreCase)))
{
    var duplicateCleanup = app.Services.GetRequiredService<Zashita.Web.Services.EmployeeDuplicateCleanupService>();
    var cleanup = duplicateCleanup.RunFullCleanup();
    Console.WriteLine("Зачистка дублей завершена.");
    Console.WriteLine($"  Пустые карточки сотрудников: {cleanup.RemovedEmployeeShells}");
    Console.WriteLine($"  Схлопнуто карточек 1С: {cleanup.CollapsedEmployeeRecords}");
    Console.WriteLine($"  Медосмотры (дубли): {cleanup.RemovedDuplicateExams}");
    Console.WriteLine($"  Списки на МО: {cleanup.RemovedPersonListLines}");
    Console.WriteLine($"  Графики МО: {cleanup.RemovedScheduleLines}");
    Console.WriteLine($"  Медограничения: {cleanup.RemovedRestrictionDocuments}");
    Console.WriteLine($"  Журнал МО: {cleanup.RemovedRegistryRows}");
    Console.WriteLine($"  Обучение (проведение): {cleanup.RemovedTrainingConduct}");
    Console.WriteLine($"  Обучение (назначения): {cleanup.RemovedTrainingAssignments}");
    Console.WriteLine($"  Уволенные (мед/списки): {cleanup.RemovedDismissedExams + cleanup.RemovedDismissedPersonListLines}");
    Console.WriteLine($"  Выровнено медосмотров: {cleanup.AlignedExams}");
    Console.WriteLine($"Итого убрано: {cleanup.TotalRemoved}");
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
            if (ex is not null)
            {
                try
                {
                    var dir = Path.Combine(app.Environment.ContentRootPath, "App_Data");
                    Directory.CreateDirectory(dir);
                    var line =
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {context.Request.Method} {context.Request.Path}\r\n{ex}\r\n\r\n";
                    await File.AppendAllTextAsync(Path.Combine(dir, "errors.log"), line);
                }
                catch
                {
                    // ignore logging failures
                }
            }

            context.Response.Redirect("/Error");
        });
    });
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });
}

app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseResponseCompression();

// HTTPS redirect only when TLS is configured (behind nginx on HTTP — skip)
var useHttpsRedirect = builder.Configuration.GetValue("UseHttpsRedirection", false);
if (useHttpsRedirect)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx => ApplyNoCacheHeaders(ctx.Context)
    });
}
else
{
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            // Версионированные файлы (?v=hash) можно кэшировать, остальные — нет.
            var path = ctx.Context.Request.Path.Value ?? string.Empty;
            if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                return;
            }

            ApplyNoCacheHeaders(ctx.Context);
        }
    });
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(state =>
    {
        var ctx = (HttpContext)state!;
        if (ctx.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)
        {
            ApplyNoCacheHeaders(ctx);
        }

        return Task.CompletedTask;
    }, context);

    await next();
});

static void ApplyNoCacheHeaders(HttpContext context)
{
    if (context.Response.HasStarted)
    {
        return;
    }

    context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";
}

app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapGet("/print/preview", (HttpContext ctx, HtmlPrintService print) =>
{
    if (!AuthHelper.IsAuthenticated(ctx))
    {
        return Results.Redirect("/Login");
    }

    var title = ctx.Request.Query["title"].FirstOrDefault() ?? "Печать";
    var sheet = ctx.Request.Query["sheet"].FirstOrDefault();
    return Results.Content(print.BuildDocument(title, sheet), "text/html; charset=utf-8");
});

app.MapGet("/print/pdf", (HttpContext ctx) =>
{
    if (!AuthHelper.IsAuthenticated(ctx))
    {
        return Results.Redirect("/Login");
    }

    var title = ctx.Request.Query["title"].FirstOrDefault() ?? "Документ";
    var message = $"PDF для «{title}» будет сформирован на сервере без Excel COM (следующий этап). " +
                  "Пока используйте предпросмотр HTML и печать в PDF через браузер.";
    return Results.Text(message, "text/plain; charset=utf-8");
});

app.MapGet("/api/search", (HttpContext ctx, GlobalSearchService search) =>
{
    if (!AuthHelper.IsAuthenticated(ctx))
    {
        return Results.Unauthorized();
    }

    var q = ctx.Request.Query["q"].FirstOrDefault();
    return Results.Json(search.Search(q));
});

app.MapPost("/api/print-history", async (
    HttpContext ctx,
    Zashita.Core.PrintHistory.DocumentPrintHistoryStore store,
    OrganizationStore orgs) =>
{
    if (!AuthHelper.IsAuthenticated(ctx))
    {
        return Results.Unauthorized();
    }

    var body = await ctx.Request.ReadFromJsonAsync<PrintHistoryPostBody>();
    var title = body?.Title?.Trim();
    if (string.IsNullOrWhiteSpace(title))
    {
        return Results.BadRequest(new { ok = false, error = "title" });
    }

    var org = string.IsNullOrWhiteSpace(body?.Organization)
        ? orgs.GetSelectedOrganization(ctx)
        : body!.Organization!.Trim();

    store.Append(
        category: body?.Category ?? "other",
        title: title,
        organization: org,
        detail: body?.Detail,
        summary: body?.Summary ?? "Печать",
        openUrl: body?.OpenUrl);

    return Results.Json(new { ok = true });
});

app.MapGet("/Med/MoSoutMapping", () => Results.Redirect("/Med/Standards"));
app.MapGet("/Med/MoSoutMapping/Edit", () => Results.Redirect("/Med/Standards"));

app.MapRazorPages();
app.Run();

static void RunStartupMaintenance(IServiceProvider services, string contentRootPath)
{
    var stampPath = Path.Combine(contentRootPath, "App_Data", "_startup-maintenance.stamp");
    var today = DateTime.Today.ToString("yyyy-MM-dd");
    try
    {
        if (File.Exists(stampPath) &&
            string.Equals(File.ReadAllText(stampPath).Trim(), today, StringComparison.Ordinal))
        {
            return;
        }
    }
    catch
    {
        // continue maintenance
    }

    var employees = services.GetRequiredService<Zashita.Web.Services.OrganizationEmployeesStore>();
    employees.ApplyLegalEntitySplits();

    var orgs = services.GetRequiredService<Zashita.Web.Services.OrganizationStore>();
    orgs.EnsurePreservedLegalEntities();
    orgs.ConsolidateOrganizationAliases();
    orgs.PurgeLegacyManualShellOrganizations();
    orgs.MergeEmployeeOrganizations();
    orgs.ConsolidateOrganizationAliases();

    var medAligner = services.GetRequiredService<Zashita.Web.Services.MedDataOrganizationAligner>();
    medAligner.AlignToCanonicalOrganizations(orgs.AllOrganizations);

    // Уволенных с датой из 1С оставляем — нужны для архива обучения.
    employees.PurgeDuplicatePersonShells();

    // Полная зачистка дублей — не чаще раза в сутки (сильно грузит CPU на 1.6 ГБ RAM).
    var duplicateCleanup = services.GetRequiredService<Zashita.Web.Services.EmployeeDuplicateCleanupService>();
    duplicateCleanup.RunFullCleanup();

    var commissionStore = services.GetRequiredService<Zashita.Core.Risks.SoutCommissionStore>();
    foreach (var org in orgs.AllOrganizations)
    {
        commissionStore.MigrateScopeSeparation(org);
    }

    // Полный аудит медосмотров убран со старта — его можно запускать вручную (--audit-med).

    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
        File.WriteAllText(stampPath, today);
    }
    catch
    {
        // ignore stamp write
    }
}

file sealed class PrintHistoryPostBody
{
    public string? Category { get; set; }
    public string? Title { get; set; }
    public string? Organization { get; set; }
    public string? Detail { get; set; }
    public string? Summary { get; set; }
    public string? OpenUrl { get; set; }
}
