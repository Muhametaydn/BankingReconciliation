using System.Net;

namespace BankingReconciliation.Tests;

public class FrontendTests : IClassFixture<BankingReconciliationWebApplicationFactory>
{
    private readonly HttpClient _client;

    public FrontendTests(BankingReconciliationWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Root_ReturnsFrontendHtml()
    {
        using var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Mutabakat Yönetimi", html);
        Assert.Contains("CSV/TXT mutabakat", html);
        Assert.Contains("compare-form", html);
        Assert.Contains("Geçmiş Mutabakatlar", html);
        Assert.Contains("history-filter-form", html);
        Assert.Contains("history-status", html);
        Assert.Contains("history-previous-button", html);
        Assert.Contains("history-next-button", html);
        Assert.Contains("Excel indir", html);
        Assert.Contains("Validasyon yap", html);
        Assert.Contains("operator-identity", html);
        Assert.Contains("Önce giriş yapın", html);
        Assert.Contains("login-form", html);
        Assert.Contains("register-form", html);
        Assert.Contains("user-management-panel", html);
        Assert.DoesNotContain("Sunumu tek tıkla hazırla", html);
        Assert.Contains("advanced-settings-toggle", html);
        Assert.Contains("workflow-guide", html);
        Assert.Contains("comparison-narrative", html);
        Assert.Contains("copy-narrative-button", html);
        Assert.Contains("validation-errors-body", html);
        Assert.Contains("require-exact-match", html);
        Assert.Contains("validation-status", html);
        Assert.Contains("Dosya Şeması", html);
        Assert.Contains("schema-list", html);
        Assert.Contains("schema-save-button", html);
        Assert.Contains("schema-status", html);
        Assert.Contains("Karşılaştırma Ayarları", html);
        Assert.Contains("comparison-settings-form", html);
        Assert.Contains("comparison-settings-save-button", html);
        Assert.Contains("Veri Kaynaklari", html);
        Assert.Contains("sources-list", html);
        Assert.Contains("database-compare-button", html);
        Assert.Contains("database-queue-button", html);
        Assert.Contains("file-queue-button", html);
        Assert.Contains("Arka planda karşılaştır", html);
        Assert.Contains("Onay Karari", html);
        Assert.Contains("approval-user-note", html);
        Assert.DoesNotContain("approval-token", html);
        Assert.Contains("approve-button", html);
        Assert.Contains("reject-button", html);
        Assert.Contains("Kullanıcı ve Rol Yönetimi", html);
        Assert.DoesNotContain("management-token", html);
        Assert.Contains("İşlem Kayıtları", html);
        Assert.Contains("audit-filter-form", html);
        Assert.Contains("audit-retention-status", html);
        Assert.Contains("audit-list", html);
        Assert.Contains("Veri kaynaklarını karşılaştır", html);
        Assert.Contains("status-filter", html);
        Assert.Contains("results-head", html);
        Assert.Contains("Farklı", html);
        Assert.Contains("app.js", html);
        Assert.Contains("styles.css?v=", html);
        Assert.Contains("app.js?v=", html);
        Assert.Contains(".csv,.txt", html);
    }

    [Fact]
    public async Task Health_ReturnsApiStatus()
    {
        using var response = await _client.GetAsync("/api/health");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Banking Reconciliation API", json);
        Assert.Contains("Running", json);
    }

    [Fact]
    public async Task Readiness_ReturnsDependencyStatus()
    {
        using var response = await _client.GetAsync("/api/health/ready");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"Ready\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"database\":\"Ready\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "\"temporaryStorage\":\"Ready\"",
            json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwaggerJson_ReturnsOpenApiDefinition()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/reconciliations/compare", json);
    }

    [Theory]
    [InlineData("/styles.css", "text/css")]
    [InlineData("/app.js", "text/javascript")]
    public async Task StaticAssets_ReturnExpectedContentType(string path, string expectedMediaType)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedMediaType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AppJs_IncludesTurkishStatusDescriptions()
    {
        using var response = await _client.GetAsync("/app.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Iki tarafta da var, adet ve tutar ayni.", script);
        Assert.Contains("Yalnızca Karşılaştırma Dosyası 1'de var.", script);
        Assert.Contains("Yalnızca Karşılaştırma Dosyası 2'de var.", script);
        Assert.Contains("Kolon:", script);
        Assert.Contains("/api/reconciliation-file-schema/validate", script);
        Assert.Contains("/api/reconciliation-file-schema", script);
        Assert.Contains("renderFileSchema", script);
        Assert.Contains("saveFileSchema", script);
        Assert.Contains("collectSchemaColumns", script);
        Assert.Contains("getSchemaControlNumber", script);
        Assert.Contains("getSchemaControlList", script);
        Assert.Contains("minLength", script);
        Assert.Contains("maxLength", script);
        Assert.Contains("minValue", script);
        Assert.Contains("maxValue", script);
        Assert.Contains("maxDecimalPlaces", script);
        Assert.Contains("fixedWidthStart", script);
        Assert.Contains("fixedWidthLength", script);
        Assert.Contains("allowedValues", script);
        Assert.Contains("method: \"PUT\"", script);
        Assert.Contains("schema-description", script);
        Assert.Contains("Validasyon başarılı.", script);
        Assert.Contains("formatValidationErrors", script);
        Assert.Contains("result.errors", script);
        Assert.Contains("fieldValues", script);
        Assert.Contains("fieldDifferences", script);
        Assert.Contains("renderResultHeader", script);
        Assert.Contains("getResultFields", script);
        Assert.Contains("getDifferenceFields", script);
        Assert.Contains("FieldMismatch", script);
        Assert.Contains("loadComparisonSettings", script);
        Assert.Contains("saveComparisonSettings", script);
        Assert.Contains("collectComparisonSettings", script);
        Assert.Contains("parseFieldMappings", script);
        Assert.Contains("Kaynak=Hedef", script);
        Assert.Contains("/api/reconciliation-comparison-settings", script);
        Assert.Contains("loadSources", script);
        Assert.Contains("saveSource", script);
        Assert.Contains("/api/reconciliation-sources", script);
        Assert.Contains("Veritabani hazir", script);
        Assert.Contains("Veritabani baglantisi eksik", script);
        Assert.Contains("compareDatabaseSources", script);
        Assert.Contains("queueDatabaseSourcesComparison", script);
        Assert.Contains("queueFilesComparison", script);
        Assert.Contains("/api/reconciliations/compare/jobs", script);
        Assert.Contains("monitorBackgroundBatch", script);
        Assert.Contains("advanced-configuration", script);
        Assert.Contains("setWorkflowStep", script);
        Assert.Contains("workflow-complete", script);
        Assert.Contains("renderComparisonNarrative", script);
        Assert.Contains("copyComparisonNarrative", script);
        Assert.Contains("copyTextWithSelection", script);
        Assert.Contains("areDatabaseSourcesReady", script);
        Assert.Contains("/api/reconciliations/compare-database-sources", script);
        Assert.Contains("createHistoryQuery", script);
        Assert.Contains("historyPageSize", script);
        Assert.Contains("İşlem tamamlanamadı.", script);
        Assert.Contains("submitApproval", script);
        Assert.Contains("/approval`,", script);
        Assert.Contains("formatApprovalStatus", script);
        Assert.Contains("loadAuditEvents", script);
        Assert.Contains("createManagementHeaders", script);
        Assert.Contains("/api/reconciliation-audit-events", script);
        Assert.Contains("renderAuditEvents", script);
        Assert.Contains("butunluk dogrulandi", script);
        Assert.Contains("butunluk hatasi", script);
        Assert.Contains("WORM: aktarildi", script);
        Assert.Contains("/api/reconciliation-audit-retention/status", script);
        Assert.Contains("Degistirilemez arsive aktarim bekliyor", script);
        Assert.Contains("WORM kuyrugu yas esigini asti", script);
    }

    [Fact]
    public async Task AppJs_AndStylesIncludeResultRowHighlighting()
    {
        using var scriptResponse = await _client.GetAsync("/app.js");
        using var stylesResponse = await _client.GetAsync("/styles.css");
        var script = await scriptResponse.Content.ReadAsStringAsync();
        var styles = await stylesResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, scriptResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stylesResponse.StatusCode);
        Assert.Contains("result-row-mismatch", script);
        Assert.Contains("result-row-branch-only", script);
        Assert.Contains("result-row-bank-only", script);
        Assert.Contains("tbody tr.result-row-mismatch", styles);
        Assert.Contains("validation-status-success", styles);
        Assert.Contains("validation-status-error", styles);
        Assert.Contains("white-space: pre-line", styles);
        Assert.Contains("schema-list", styles);
        Assert.Contains("schema-item", styles);
        Assert.Contains("schema-editor", styles);
        Assert.Contains("schema-field", styles);
        Assert.Contains("schema-description", styles);
        Assert.Contains(".status.FieldMismatch", styles);
        Assert.Contains(".approval-panel", styles);
        Assert.Contains(".approval-badge.Approved", styles);
        Assert.Contains("button.danger", styles);
        Assert.Contains(".management-access-panel", styles);
        Assert.Contains(".audit-list", styles);
        Assert.Contains(".audit-retention-status-degraded", styles);
        Assert.Contains(".audit-state-grid", styles);
        Assert.Contains("button:disabled", styles);
        Assert.Contains("cursor: not-allowed", styles);
        Assert.DoesNotContain("cursor: wait", styles);
    }
}
