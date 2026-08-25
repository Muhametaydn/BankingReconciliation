const form = document.querySelector("#compare-form");
const operatorIdentity = document.querySelector("#operator-identity");
const loginForm = document.querySelector("#login-form");
const loginUsername = document.querySelector("#login-username");
const loginPassword = document.querySelector("#login-password");
const registerForm = document.querySelector("#register-form");
const registerUsername = document.querySelector("#register-username");
const registerPassword = document.querySelector("#register-password");
const authenticationForms = document.querySelector("#authentication-forms");
const authenticatedSession = document.querySelector("#authenticated-session");
const authenticatedUsername = document.querySelector("#authenticated-username");
const authenticatedRole = document.querySelector("#authenticated-role");
const authenticationStatus = document.querySelector("#authentication-status");
const logoutButton = document.querySelector("#logout-button");
const userManagementPanel = document.querySelector("#user-management-panel");
const refreshUsersButton = document.querySelector("#refresh-users-button");
const userManagementStatus = document.querySelector("#user-management-status");
const userList = document.querySelector("#user-list");
const advancedSettingsToggle = document.querySelector("#advanced-settings-toggle");
const comparisonNarrative = document.querySelector("#comparison-narrative");
const comparisonNarrativeText = document.querySelector("#comparison-narrative-text");
const copyNarrativeButton = document.querySelector("#copy-narrative-button");
const copyNarrativeStatus = document.querySelector("#copy-narrative-status");
const branchFileInput = document.querySelector("#branch-file");
const bankFileInput = document.querySelector("#bank-file");
const branchFileName = document.querySelector("#branch-file-name");
const bankFileName = document.querySelector("#bank-file-name");
const compareButton = document.querySelector("#compare-button");
const fileQueueButton = document.querySelector("#file-queue-button");
const validateButton = document.querySelector("#validate-button");
const resetButton = document.querySelector("#reset-button");
const alertBox = document.querySelector("#alert");
const validationStatus = document.querySelector("#validation-status");
const validationDetails = document.querySelector("#validation-details");
const validationErrorCount = document.querySelector("#validation-error-count");
const validationErrorsBody = document.querySelector("#validation-errors-body");
const resultsHead = document.querySelector("#results-head");
const resultsBody = document.querySelector("#results-body");
const exportButton = document.querySelector("#export-button");
const selectedBatchInfo = document.querySelector("#selected-batch-info");
const approvalStatus = document.querySelector("#approval-status");
const approvalDecisionMeta = document.querySelector("#approval-decision-meta");
const approvalUserNote = document.querySelector("#approval-user-note");
const approvalComment = document.querySelector("#approval-comment");
const approveButton = document.querySelector("#approve-button");
const rejectButton = document.querySelector("#reject-button");
const approvalFeedback = document.querySelector("#approval-feedback");
const schemaList = document.querySelector("#schema-list");
const schemaStatus = document.querySelector("#schema-status");
const schemaSaveButton = document.querySelector("#schema-save-button");
const schemaAddButton = document.querySelector("#schema-add-button");
const comparisonSettingsStatus = document.querySelector("#comparison-settings-status");
const comparisonSettingsSaveButton = document.querySelector("#comparison-settings-save-button");
const sourcesList = document.querySelector("#sources-list");
const sourcesStatus = document.querySelector("#sources-status");
const databaseCompareButton = document.querySelector("#database-compare-button");
const databaseQueueButton = document.querySelector("#database-queue-button");
const historyList = document.querySelector("#history-list");
const refreshHistoryButton = document.querySelector("#refresh-history-button");
const historyFilterForm = document.querySelector("#history-filter-form");
const historySearch = document.querySelector("#history-search");
const historyFrom = document.querySelector("#history-from");
const historyTo = document.querySelector("#history-to");
const historyStatus = document.querySelector("#history-status");
const historyClearButton = document.querySelector("#history-clear-button");
const historyPreviousButton = document.querySelector("#history-previous-button");
const historyNextButton = document.querySelector("#history-next-button");
const historyPageInfo = document.querySelector("#history-page-info");
const statusFilter = document.querySelector("#status-filter");
const auditRefreshButton = document.querySelector("#audit-refresh-button");
const auditFilterForm = document.querySelector("#audit-filter-form");
const auditActor = document.querySelector("#audit-actor");
const auditAction = document.querySelector("#audit-action");
const auditResourceType = document.querySelector("#audit-resource-type");
const auditStatus = document.querySelector("#audit-status");
const auditRetentionStatus = document.querySelector("#audit-retention-status");
const auditList = document.querySelector("#audit-list");
let selectedBatchId = null;
let selectedBatch = null;
let currentResults = [];
let currentSchemaColumns = [];
let currentSources = [];
let currentHistory = [];
let currentComparisonSettings = null;
let runtimeSettings = {
    synchronousComparisonMaxFileSizeBytes: 1024 * 1024,
    maxCsvFileSizeBytes: 5 * 1024 * 1024
};
let historyRefreshInProgress = false;
let accessToken = sessionStorage.getItem("reconciliationAccessToken") ?? "";
let currentUser = null;
const defaultResultFields = ["BranchCode", "FundCode", "TransactionNumber"];
const requiredCoreFields = new Set(defaultResultFields.concat(["TransactionDate", "Quantity", "Amount"]));
const historyPageSize = 10;
let historyPage = 0;

const counters = {
    totalBranchRecords: document.querySelector("#total-branch"),
    totalBankRecords: document.querySelector("#total-bank"),
    matchedCount: document.querySelector("#matched-count"),
    mismatchCount: document.querySelector("#mismatch-count"),
    onlyInBranchCount: document.querySelector("#only-branch-count"),
    onlyInBankCount: document.querySelector("#only-bank-count"),
    resultCount: document.querySelector("#result-count")
};

loginForm.addEventListener("submit", login);
registerForm.addEventListener("submit", register);
logoutButton.addEventListener("click", logout);
refreshUsersButton.addEventListener("click", loadUsers);
userList.addEventListener("change", updateUserRole);

branchFileInput.addEventListener("change", () => {
    updateFileName(branchFileInput, branchFileName);
    updateFileSelectionWorkflow();
});

bankFileInput.addEventListener("change", () => {
    updateFileName(bankFileInput, bankFileName);
    updateFileSelectionWorkflow();
});

copyNarrativeButton.addEventListener("click", copyComparisonNarrative);
advancedSettingsToggle.addEventListener("click", () => {
    const shouldShow = advancedSettingsToggle.getAttribute("aria-expanded") !== "true";
    for (const section of document.querySelectorAll(".advanced-configuration")) {
        const requiresAdministrator = section.dataset.adminOnly === "true";
        section.hidden = !shouldShow || (requiresAdministrator && currentUser?.role !== "Administrator");
    }
    advancedSettingsToggle.setAttribute("aria-expanded", String(shouldShow));
    advancedSettingsToggle.textContent = shouldShow
        ? "Gelişmiş ayarları gizle"
        : "Gelişmiş ayarları göster";
});

resetButton.addEventListener("click", () => {
    form.reset();
    branchFileName.textContent = "Dosya seçilmedi";
    bankFileName.textContent = "Dosya seçilmedi";
    hideAlert();
    hideValidationStatus();
    selectedBatchId = null;
    selectedBatch = null;
    approvalComment.value = "";
    hideApprovalFeedback();
    currentResults = [];
    statusFilter.value = "All";
    renderSummary();
    renderFilteredResults();
    updateSelectedBatchInfo();
    setWorkflowStep(2, "pending");
    setWorkflowStep(3, "pending");
    setWorkflowStep(4, "pending");
    setWorkflowStep(5, "pending");
});

validateButton.addEventListener("click", async () => {
    hideAlert();
    hideValidationStatus();

    const branchFile = branchFileInput.files[0];
    const bankFile = bankFileInput.files[0];

    if (!branchFile || !bankFile) {
        showAlert("Validasyon için Karşılaştırma Dosyası 1 ve 2'yi seçin.");
        return;
    }

    setBusy(true, "Doğrulanıyor");

    try {
        const branchResult = await validateFile(branchFile);
        const bankResult = await validateFile(bankFile);

        if (branchResult.isValid && bankResult.isValid) {
            showValidationStatus(
                `Validasyon başarılı. Dosya 1: ${branchResult.recordCount} kayıt, Dosya 2: ${bankResult.recordCount} kayıt.`,
                "success");
            renderValidationErrors([]);
            setWorkflowStep(3, "complete");
            return;
        }

        const validationErrors = [
            ...mapValidationErrors("Dosya 1", branchResult),
            ...mapValidationErrors("Dosya 2", bankResult)
        ];
        showValidationStatus(`${validationErrors.length} validasyon hatası bulundu. Ayrıntılar aşağıdadır.`, "error");
        renderValidationErrors(validationErrors);
        setWorkflowStep(3, "error");
    } catch (error) {
        showAlert(error.message || getNetworkErrorMessage());
    } finally {
        setBusy(false);
    }
});

exportButton.addEventListener("click", () => {
    if (!selectedBatchId) {
        return;
    }

    window.location.href = `/api/reconciliations/${selectedBatchId}/export`;
});

refreshHistoryButton.addEventListener("click", async () => {
    await loadHistory();
});

historyFilterForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    historyPage = 0;
    await loadHistory();
});

historyClearButton.addEventListener("click", async () => {
    historyFilterForm.reset();
    historyPage = 0;
    await loadHistory();
});

historyPreviousButton.addEventListener("click", async () => {
    historyPage = Math.max(0, historyPage - 1);
    await loadHistory();
});

historyNextButton.addEventListener("click", async () => {
    historyPage++;
    await loadHistory();
});

schemaSaveButton.addEventListener("click", async () => {
    await saveFileSchema();
});

schemaAddButton.addEventListener("click", () => {
    const existing = collectSchemaColumns();
    let suffix = 1;
    while (existing.some(column => column.field.toLowerCase() === `extrafield${suffix}`.toLowerCase())) {
        suffix++;
    }
    existing.push({
        position: existing.length + 1,
        field: `ExtraField${suffix}`,
        name: `ExtraField${suffix}`,
        type: "Text",
        required: true,
        allowedValues: [],
        description: "Kullanıcı tanımlı ek kolon."
    });
    renderFileSchema(existing);
    showSchemaStatus("Yeni kolon zorunlu olarak eklendi. Sayısal kolonlar kaydedildiğinde karşılaştırmaya otomatik katılır.", "success");
});

schemaList.addEventListener("click", event => {
    const actionButton = event.target.closest("[data-schema-action]");
    if (!actionButton) return;
    const columns = collectSchemaColumns();
    const index = Number(actionButton.dataset.schemaIndex);
    if (!Number.isInteger(index) || index < 0 || index >= columns.length) return;
    const action = actionButton.dataset.schemaAction;
    if (action === "remove") {
        if (requiredCoreFields.has(columns[index].field)) {
            showSchemaStatus("Zorunlu çekirdek kolonlar silinemez.", "error");
            return;
        }
        columns.splice(index, 1);
    } else if (action === "up" && index > 0) {
        [columns[index - 1], columns[index]] = [columns[index], columns[index - 1]];
    } else if (action === "down" && index < columns.length - 1) {
        [columns[index + 1], columns[index]] = [columns[index], columns[index + 1]];
    }
    renderFileSchema(columns);
});

schemaList.addEventListener("change", event => {
    if (event.target.matches('[data-schema-key="type"]')) {
        updateSchemaTypeVisibility(event.target.closest(".schema-item"));
    }
});

comparisonSettingsSaveButton.addEventListener("click", async () => {
    await saveComparisonSettings();
});

document.querySelector("#comparison-settings-form").addEventListener("click", event => {
    const addButton = event.target.closest("[data-add-mapping]");
    if (addButton) {
        addMappingRow(addButton.dataset.addMapping);
        return;
    }
    const removeButton = event.target.closest("[data-remove-mapping]");
    if (removeButton) {
        removeButton.closest(".mapping-row")?.remove();
    }
});

statusFilter.addEventListener("change", () => {
    renderFilteredResults();
});

auditRefreshButton.addEventListener("click", async () => {
    await loadAuditEvents();
});

auditFilterForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    await loadAuditEvents();
});

approveButton.addEventListener("click", async () => {
    await submitApproval("Approve");
});

rejectButton.addEventListener("click", async () => {
    await submitApproval("Reject");
});

historyList.addEventListener("click", async (event) => {
    const button = event.target.closest("[data-batch-id]");
    if (!button) {
        return;
    }

    await loadBatch(button.dataset.batchId);
});

sourcesList.addEventListener("click", async (event) => {
    const button = event.target.closest("[data-source-save]");
    if (button) {
        await saveSource(button.dataset.sourceSave);
    }
});

databaseCompareButton.addEventListener("click", async () => {
    await compareDatabaseSources();
});

databaseQueueButton.addEventListener("click", async () => {
    await queueDatabaseSourcesComparison();
});

fileQueueButton.addEventListener("click", async () => {
    await queueFilesComparison();
});

form.addEventListener("submit", async (event) => {
    event.preventDefault();
    hideAlert();

    const branchFile = branchFileInput.files[0];
    const bankFile = bankFileInput.files[0];

    if (!branchFile || !bankFile) {
        showAlert("Karşılaştırma Dosyası 1 ve 2'yi seçin.");
        return;
    }

    if (Math.max(branchFile.size, bankFile.size) > runtimeSettings.synchronousComparisonMaxFileSizeBytes) {
        showValidationStatus("Dosya boyutu yüksek olduğu için işlem otomatik olarak arka plana alındı.", "success");
        await queueFilesComparison(true);
        return;
    }

    const body = new FormData();
    body.append("branchFile", branchFile);
    body.append("bankFile", bankFile);

    setBusy(true, "Karşılaştırılıyor");

    try {
        const response = await fetch("/api/reconciliations/compare", {
            method: "POST",
            headers: createInitiatorHeaders(),
            body
        });

        const payload = await response.json();

        if (!response.ok) {
            showAlert(formatError(payload));
            return;
        }

        applyComparisonResult(payload);
        await loadHistory();
    } catch {
        showAlert(getNetworkErrorMessage());
    } finally {
        setBusy(false);
    }
});

async function queueFilesComparison(wasAutomaticallySelected = false) {
    hideAlert();
    hideValidationStatus();

    const branchFile = branchFileInput.files[0];
    const bankFile = bankFileInput.files[0];
    if (!branchFile || !bankFile) {
        showAlert("Arka plan mutabakatı için Karşılaştırma Dosyası 1 ve 2'yi seçin.");
        return;
    }

    const body = new FormData();
    body.append("branchFile", branchFile);
    body.append("bankFile", bankFile);
    setBusy(true, "Kuyruga aliniyor");

    try {
        const response = await fetch("/api/reconciliations/compare/jobs", {
            method: "POST",
            headers: createInitiatorHeaders(),
            body
        });
        const payload = await response.json();
        if (!response.ok) {
            showAlert(formatError(payload));
            return;
        }

        selectedBatchId = payload.batchId;
        selectedBatch = {
            batchId: payload.batchId,
            batchStatus: payload.status,
            approvalStatus: "NotApplicable"
        };
        setWorkflowStep(4, "active");
        updateSelectedBatchInfo(selectedBatch);
        showValidationStatus(
            wasAutomaticallySelected
                ? "Büyük dosya karşılaştırması arka planda başlatıldı. Sonuçlar otomatik güncellenecek."
                : "Dosya karşılaştırması arka planda başlatıldı. Sonuçlar otomatik güncellenecek.",
            "success");
        await loadHistory();
        monitorBackgroundBatch(payload.batchId, showValidationStatus)
            .catch(error => showAlert(error.message || getNetworkErrorMessage()));
    } catch (error) {
        showAlert(error.message || getNetworkErrorMessage());
    } finally {
        setBusy(false);
    }
}

function updateFileName(input, target) {
    target.textContent = input.files[0]?.name ?? "Dosya seçilmedi";
}

function updateFileSelectionWorkflow() {
    const bothSelected = Boolean(branchFileInput.files[0] && bankFileInput.files[0]);
    setWorkflowStep(2, bothSelected ? "complete" : "active");
    setWorkflowStep(3, "pending");
    setWorkflowStep(4, "pending");
    setWorkflowStep(5, "pending");
}

function setWorkflowStep(step, status) {
    const item = document.querySelector(`[data-workflow-step="${step}"]`);
    if (!item) return;
    item.classList.remove("workflow-complete", "workflow-active", "workflow-error");
    if (status === "complete") item.classList.add("workflow-complete");
    if (status === "active") item.classList.add("workflow-active");
    if (status === "error") item.classList.add("workflow-error");
}

function getNetworkErrorMessage() {
    if (window.location.protocol === "file:") {
        return "Sayfayi dosya olarak acmissiniz. Uygulamayi F5 ile baslatin ve http://localhost:5230 adresinden acin.";
    }

    return "API istegi tamamlanamadi. Uygulamanin calistigindan ve sayfayi http://localhost:5230 adresinden actiginizdan emin olun.";
}

async function login(event) {
    event.preventDefault();
    await authenticate("/api/auth/login", loginUsername.value, loginPassword.value, "Giriş başarılı.");
    loginPassword.value = "";
}

async function register(event) {
    event.preventDefault();
    await authenticate("/api/auth/register", registerUsername.value, registerPassword.value, "Kayıt tamamlandı.");
    registerPassword.value = "";
}

async function authenticate(endpoint, username, password, successMessage) {
    hideAuthenticationStatus();
    try {
        const response = await fetch(endpoint, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ username: username.trim(), password })
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
            const message = response.status === 401
                ? "Kullanıcı adı veya parola hatalı."
                : formatError(payload);
            throw new Error(message);
        }

        applyAuthenticationSession(payload.accessToken, payload.user);
        const firstAdminMessage = payload.isFirstAdministrator
            ? " İlk kullanıcı olduğunuz için Admin yetkisi verildi."
            : "";
        showAuthenticationStatus(successMessage + firstAdminMessage, "success");
    } catch (error) {
        showAuthenticationStatus(error.message || getNetworkErrorMessage(), "error");
    }
}

async function restoreAuthenticationSession() {
    if (!accessToken) {
        updateAuthenticationUi();
        return;
    }

    try {
        const response = await fetch("/api/auth/session", { headers: createAuthorizationHeaders() });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error();
        currentUser = payload;
        updateAuthenticationUi();
    } catch {
        clearAuthenticationSession();
        updateAuthenticationUi();
    }
}

function applyAuthenticationSession(token, user) {
    accessToken = token;
    currentUser = user;
    sessionStorage.setItem("reconciliationAccessToken", token);
    updateAuthenticationUi();
}

function logout() {
    clearAuthenticationSession();
    updateAuthenticationUi();
    showAuthenticationStatus("Oturum kapatıldı.", "success");
}

function clearAuthenticationSession() {
    accessToken = "";
    currentUser = null;
    sessionStorage.removeItem("reconciliationAccessToken");
}

function updateAuthenticationUi() {
    const isAuthenticated = Boolean(accessToken && currentUser);
    const isAdministrator = currentUser?.role === "Administrator";
    authenticationForms.hidden = isAuthenticated;
    authenticatedSession.hidden = !isAuthenticated;
    authenticatedUsername.textContent = currentUser?.username ?? "";
    authenticatedRole.textContent = currentUser ? formatUserRole(currentUser.role) : "";
    authenticatedRole.className = `role-badge role-${String(currentUser?.role ?? "none").toLowerCase()}`;
    operatorIdentity.value = currentUser?.username ?? "";
    setWorkflowStep(1, isAuthenticated ? "complete" : "active");
    compareButton.disabled = !isAuthenticated;
    fileQueueButton.disabled = !isAuthenticated;
    databaseCompareButton.disabled = !isAuthenticated || !areDatabaseSourcesReady();
    databaseQueueButton.disabled = !isAuthenticated || !areDatabaseSourcesReady();
    approvalUserNote.textContent = currentUser?.role === "Approver"
        ? `${currentUser.username} Approver olarak karar verebilir.`
        : "Onay veya red için Approver hesabıyla giriş yapın.";
    updateApprovalActions();
    setAdministratorControls(isAdministrator);

    if (!isAdministrator) {
        userManagementPanel.hidden = true;
    } else if (advancedSettingsToggle.getAttribute("aria-expanded") === "true") {
        userManagementPanel.hidden = false;
        loadUsers();
    }
}

function setAdministratorControls(isAdministrator) {
    const selectors = [
        ".schema-section input", ".schema-section select", ".schema-section textarea", ".schema-section button",
        ".settings-section input", ".settings-section select", ".settings-section textarea", ".settings-section button",
        ".sources-section [data-source-field]", ".sources-section [data-source-save]"
    ];
    for (const control of document.querySelectorAll(selectors.join(","))) {
        if (control.dataset.adminOriginalDisabled === undefined) {
            control.dataset.adminOriginalDisabled = String(control.disabled);
        }
        control.disabled = !isAdministrator || control.dataset.adminOriginalDisabled === "true";
    }
}

function createAuthorizationHeaders(includeContentType = false) {
    const headers = accessToken ? { "Authorization": `Bearer ${accessToken}` } : {};
    if (includeContentType) headers["Content-Type"] = "application/json";
    return headers;
}

function showAuthenticationStatus(message, status) {
    authenticationStatus.textContent = message;
    authenticationStatus.className = `validation-status validation-status-${status}`;
    authenticationStatus.hidden = false;
}

function hideAuthenticationStatus() {
    authenticationStatus.hidden = true;
    authenticationStatus.textContent = "";
}

async function loadUsers() {
    if (currentUser?.role !== "Administrator") return;
    refreshUsersButton.disabled = true;
    try {
        const response = await fetch("/api/auth/users", { headers: createAuthorizationHeaders() });
        const payload = await response.json().catch(() => ([]));
        if (!response.ok) throw new Error(formatManagementError(response.status, payload));
        renderUsers(payload);
        showUserManagementStatus(`${payload.length} kullanıcı listelendi.`, "success");
    } catch (error) {
        showUserManagementStatus(error.message || getNetworkErrorMessage(), "error");
    } finally {
        refreshUsersButton.disabled = false;
    }
}

function renderUsers(users) {
    userList.replaceChildren();
    for (const user of users) {
        const item = document.createElement("article");
        item.className = "user-item";
        const identity = document.createElement("div");
        const name = document.createElement("strong");
        name.textContent = user.username;
        const createdAt = document.createElement("small");
        createdAt.textContent = `Kayıt: ${formatDateTime(user.createdAt)}`;
        identity.append(name, createdAt);
        const role = document.createElement("select");
        role.dataset.userRole = user.id;
        for (const value of ["Operator", "Approver", "Administrator"]) {
            const option = document.createElement("option");
            option.value = value;
            option.textContent = formatUserRole(value);
            option.selected = value === user.role;
            role.append(option);
        }
        if (user.id === currentUser?.id) role.disabled = true;
        item.append(identity, role);
        userList.append(item);
    }
}

async function updateUserRole(event) {
    const select = event.target.closest("[data-user-role]");
    if (!select) return;
    select.disabled = true;
    try {
        const response = await fetch(`/api/auth/users/${select.dataset.userRole}/role`, {
            method: "PUT",
            headers: createAuthorizationHeaders(true),
            body: JSON.stringify({ role: select.value })
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(formatError(payload));
        showUserManagementStatus(`${payload.username} kullanıcısının rolü güncellendi. Yeniden giriş yapmalıdır.`, "success");
        await loadUsers();
    } catch (error) {
        showUserManagementStatus(error.message || getNetworkErrorMessage(), "error");
        await loadUsers();
    }
}

function showUserManagementStatus(message, status) {
    userManagementStatus.textContent = message;
    userManagementStatus.className = `validation-status validation-status-${status}`;
    userManagementStatus.hidden = false;
}

function formatUserRole(role) {
    if (role === "Administrator") return "Admin";
    if (role === "Approver") return "Onaylayıcı";
    return "Operatör";
}

async function validateFile(file) {
    const body = new FormData();
    body.append("file", file);

    const response = await fetch("/api/reconciliation-file-schema/validate", {
        method: "POST",
        body
    });
    const payload = await response.json();

    if (!response.ok) {
        throw new Error(formatError(payload));
    }

    return payload;
}

function createInitiatorHeaders() {
    return createAuthorizationHeaders();
}

function setBusy(isBusy, label = "Karşılaştırılıyor") {
    compareButton.disabled = isBusy || !currentUser;
    fileQueueButton.disabled = isBusy || !currentUser;
    validateButton.disabled = isBusy;
    compareButton.textContent = isBusy ? label : "Karşılaştır";
    fileQueueButton.textContent = isBusy ? label : "Arka planda karşılaştır";
    validateButton.textContent = isBusy ? label : "Validasyon yap";
    databaseCompareButton.disabled = isBusy || !currentUser || !areDatabaseSourcesReady();
    databaseQueueButton.disabled = isBusy || !currentUser || !areDatabaseSourcesReady();
}

function applyComparisonResult(payload) {
    selectedBatchId = payload.batchId ?? null;
    selectedBatch = selectedBatchId ? payload : null;
    renderSummary(payload);
    setResults(payload.results ?? []);
    updateSelectedBatchInfo(payload);
    setWorkflowStep(4, "complete");
    if (payload.approvalStatus === "Approved" || payload.approvalStatus === "Rejected") {
        setWorkflowStep(5, "complete");
    }
}

function showAlert(message) {
    alertBox.textContent = message;
    alertBox.hidden = false;
}

function hideAlert() {
    alertBox.hidden = true;
    alertBox.textContent = "";
}

function showValidationStatus(message, status) {
    validationStatus.textContent = message;
    validationStatus.className = `validation-status validation-status-${status}`;
    validationStatus.hidden = false;
}

function hideValidationStatus() {
    validationStatus.hidden = true;
    validationStatus.textContent = "";
    validationStatus.className = "validation-status";
    renderValidationErrors([]);
}

function formatError(error) {
    if (!error) {
        return "Beklenmeyen bir hata olustu.";
    }

    const parts = [error.message ?? error.error ?? "İşlem tamamlanamadı."];

    if (error.rowNumber) {
        parts.push(`Satir: ${error.rowNumber}`);
    }

    if (error.columnName) {
        parts.push(`Kolon: ${error.columnName}`);
    }

    if (error.matchingKey) {
        parts.push(`Anahtar: ${error.matchingKey}`);
    }

    return parts.join(" ");
}

function mapValidationErrors(side, result) {
    const errors = result.errors?.length ? result.errors : [result];
    if (result.isValid) return [];
    return errors.map(error => ({
        side,
        rowNumber: error.rowNumber ?? "-",
        columnName: error.columnName ?? "-",
        rule: error.rule ?? "Kolon kuralı",
        message: error.message ?? "Geçersiz değer."
    }));
}

function formatValidationErrors(side, result) {
    return mapValidationErrors(side, result)
        .map(error => `${error.side}: Satır ${error.rowNumber}, ${error.columnName}, ${error.rule}: ${error.message}`)
        .join("\n");
}

function renderValidationErrors(errors) {
    validationErrorsBody.replaceChildren();
    validationDetails.hidden = errors.length === 0;
    validationErrorCount.textContent = errors.length ? `${errors.length} hata` : "";
    for (const error of errors) {
        const row = document.createElement("tr");
        for (const value of [error.side, error.rowNumber, error.columnName, error.rule, error.message]) {
            const cell = document.createElement("td");
            cell.textContent = value;
            row.append(cell);
        }
        validationErrorsBody.append(row);
    }
}

function renderSummary(summary = {}) {
    counters.totalBranchRecords.textContent = summary.totalBranchRecords ?? 0;
    counters.totalBankRecords.textContent = summary.totalBankRecords ?? 0;
    counters.matchedCount.textContent = summary.matchedCount ?? 0;
    counters.mismatchCount.textContent = summary.mismatchCount ?? 0;
    counters.onlyInBranchCount.textContent = summary.onlyInBranchCount ?? 0;
    counters.onlyInBankCount.textContent = summary.onlyInBankCount ?? 0;
    const policyResult = document.querySelector("#policy-result");
    const policyMetric = document.querySelector("#policy-metric");
    if (summary.batchId || summary.id || summary.totalBranchRecords || summary.totalBankRecords) {
        const passed = Boolean(summary.isExactMatch);
        const requiresExactMatch = Boolean(currentComparisonSettings?.requireExactMatch);
        policyResult.textContent = requiresExactMatch
            ? (passed ? "BAŞARILI" : "BAŞARISIZ")
            : (passed ? "BİREBİR" : "İNCELEME");
        policyMetric.classList.toggle("matched", passed);
        policyMetric.classList.toggle("mismatch", !passed);
        renderComparisonNarrative(summary, passed, requiresExactMatch);
    } else {
        policyResult.textContent = "-";
        policyMetric.classList.remove("matched", "mismatch");
        comparisonNarrative.hidden = true;
        comparisonNarrativeText.textContent = "";
    }
}

function renderComparisonNarrative(summary, passed, requiresExactMatch) {
    const totalOne = Number(summary.totalBranchRecords ?? 0);
    const totalTwo = Number(summary.totalBankRecords ?? 0);
    const matched = Number(summary.matchedCount ?? 0);
    const mismatched = Number(summary.mismatchCount ?? 0);
    const onlyOne = Number(summary.onlyInBranchCount ?? 0);
    const onlyTwo = Number(summary.onlyInBankCount ?? 0);
    const policyText = requiresExactMatch
        ? (passed ? "Birebir eşleşme politikası başarılıdır." : "Birebir eşleşme politikası başarısızdır.")
        : (passed ? "Dosyalar birebir eşleşmiştir." : "Farklar inceleme için raporlanmıştır.");
    comparisonNarrativeText.textContent =
        `Dosya 1'de ${totalOne}, Dosya 2'de ${totalTwo} kayıt karşılaştırıldı. ` +
        `${matched} kayıt eşleşti; ${mismatched} kayıtta değer farkı bulundu. ` +
        `Yalnızca Dosya 1'de ${onlyOne}, yalnızca Dosya 2'de ${onlyTwo} kayıt var. ${policyText}`;
    comparisonNarrative.hidden = false;
    copyNarrativeStatus.hidden = true;
}

async function copyComparisonNarrative() {
    const text = comparisonNarrativeText.textContent;
    let copied = false;
    try {
        await navigator.clipboard.writeText(text);
        copied = true;
    } catch {
        copied = false;
    }

    const fallbackCopied = copyTextWithSelection(text);
    if (copied || fallbackCopied) {
        copyNarrativeStatus.textContent = "Özet panoya kopyalandı.";
        copyNarrativeStatus.className = "validation-status validation-status-success";
    } else {
        copyNarrativeStatus.textContent = "Özet kopyalanamadı; metni seçerek kopyalayabilirsiniz.";
        copyNarrativeStatus.className = "validation-status validation-status-error";
    }
    copyNarrativeStatus.hidden = false;
}

function copyTextWithSelection(text) {
    const textArea = document.createElement("textarea");
    textArea.value = text;
    textArea.setAttribute("readonly", "");
    textArea.style.position = "fixed";
    textArea.style.opacity = "0";
    document.body.append(textArea);
    try {
        textArea.select();
        return document.execCommand("copy");
    } catch {
        return false;
    } finally {
        textArea.remove();
    }
}

async function loadHistory() {
    try {
        const query = createHistoryQuery();
        const response = await fetch(`/api/reconciliations?${query}`);
        const payload = await response.json();

        if (!response.ok) {
            showAlert(formatError(payload));
            return;
        }

        currentHistory = payload;
        renderHistory(payload);
        const totalCount = Number(response.headers.get("X-Total-Count") ?? payload.length);
        const totalPages = Math.max(1, Math.ceil(totalCount / historyPageSize));
        historyPreviousButton.disabled = historyPage === 0;
        historyNextButton.disabled = historyPage + 1 >= totalPages;
        historyPageInfo.textContent = `Sayfa ${historyPage + 1} / ${totalPages}`;
    } catch {
        showAlert(getNetworkErrorMessage());
    }
}

async function loadRuntimeSettings() {
    try {
        const response = await fetch("/api/reconciliation-runtime-settings");
        if (response.ok) {
            runtimeSettings = await response.json();
        }
    } catch {
        // Güvenli istemci varsayılanları kullanılmaya devam eder.
    }
}

async function refreshActiveBackgroundJobs() {
    if (historyRefreshInProgress || document.hidden || !currentHistory.some(item =>
        item.status === "Queued" || item.status === "Processing")) {
        return;
    }

    historyRefreshInProgress = true;
    try {
        await loadHistory();
        const activeSelection = currentHistory.find(item => item.id === selectedBatchId &&
            (item.status === "Queued" || item.status === "Processing"));
        if (activeSelection) {
            await loadBatch(selectedBatchId);
        }
    } finally {
        historyRefreshInProgress = false;
    }
}

function createHistoryQuery() {
    const query = new URLSearchParams({
        skip: String(historyPage * historyPageSize),
        take: String(historyPageSize)
    });

    if (historySearch.value.trim()) {
        query.set("search", historySearch.value.trim());
    }
    if (historyFrom.value) {
        query.set("from", new Date(`${historyFrom.value}T00:00:00`).toISOString());
    }
    if (historyTo.value) {
        query.set("to", new Date(`${historyTo.value}T23:59:59.999`).toISOString());
    }
    if (historyStatus.value) {
        query.set("status", historyStatus.value);
    }

    return query.toString();
}

async function loadFileSchema() {
    try {
        const response = await fetch("/api/reconciliation-file-schema");
        const payload = await response.json();

        if (!response.ok) {
            return;
        }

        hideSchemaStatus();
        renderFileSchema(payload);
    } catch {
        renderFileSchema([]);
    }
}

async function loadComparisonSettings() {
    try {
        const response = await fetch("/api/reconciliation-comparison-settings");
        const payload = await response.json();

        if (response.ok) {
            renderComparisonSettings(payload);
        }
    } catch {
        showComparisonSettingsStatus(getNetworkErrorMessage(), "error");
    }
}

async function loadSources() {
    try {
        const response = await fetch("/api/reconciliation-sources");
        const payload = await response.json();
        if (response.ok) {
            renderSources(payload);
        }
    } catch {
        showSourcesStatus(getNetworkErrorMessage(), "error");
    }
}

function renderSources(sources = []) {
    currentSources = sources;
    sourcesList.replaceChildren();
    if (sources.length === 0) {
        const empty = document.createElement("p");
        empty.className = "empty-state sources-empty";
        empty.textContent = "Veri kaynagi bulunamadi.";
        sourcesList.append(empty);
        updateDatabaseCompareButton();
        return;
    }

    for (const source of sources) {
        const item = document.createElement("div");
        item.className = "source-item";
        item.dataset.sourceId = source.id;

        const title = document.createElement("div");
        title.className = "source-title";
        const type = document.createElement("strong");
        type.textContent = source.code === "BRANCH"
            ? "Karşılaştırma Dosyası 1"
            : source.code === "BANK"
                ? "Karşılaştırma Dosyası 2"
                : source.displayName;
        const code = document.createElement("span");
        code.className = "source-code";
        code.textContent = `${source.code} | ${source.isDatabaseConfigured ? "Veritabani hazir" : "Veritabani baglantisi eksik"}`;
        title.append(type, code);

        const displayName = createSourceField("Gorunen ad", "displayName", source.displayName);
        const description = createSourceField("Aciklama", "description", source.description, true);

        const actions = document.createElement("div");
        actions.className = "source-actions";
        const activeLabel = document.createElement("label");
        activeLabel.className = "settings-check";
        const active = document.createElement("input");
        active.type = "checkbox";
        active.checked = Boolean(source.isActive);
        active.dataset.sourceField = "isActive";
        const activeText = document.createElement("span");
        activeText.textContent = "Aktif";
        activeLabel.append(active, activeText);
        const saveButton = document.createElement("button");
        saveButton.type = "button";
        saveButton.className = "secondary";
        saveButton.dataset.sourceSave = source.id;
        saveButton.textContent = "Kaydet";
        actions.append(activeLabel, saveButton);

        item.append(title, displayName, description, actions);
        sourcesList.append(item);
    }

    updateDatabaseCompareButton();
    setAdministratorControls(currentUser?.role === "Administrator");
}

function areDatabaseSourcesReady() {
    return ["BRANCH", "BANK"].every(code => currentSources.some(source =>
        source.code === code && source.isActive && source.isDatabaseConfigured));
}

function updateDatabaseCompareButton() {
    const isReady = areDatabaseSourcesReady();
    databaseCompareButton.disabled = !currentUser || !isReady;
    databaseQueueButton.disabled = !currentUser || !isReady;
}

async function compareDatabaseSources() {
    hideAlert();
    if (!areDatabaseSourcesReady()) {
        showSourcesStatus("İki veri kaynağı da aktif ve hazır olmalıdır.", "error");
        return;
    }

    databaseCompareButton.disabled = true;
    databaseCompareButton.textContent = "Karşılaştırılıyor";
    try {
        const response = await fetch("/api/reconciliations/compare-database-sources", {
            method: "POST",
            headers: createInitiatorHeaders()
        });
        const payload = await response.json();
        if (!response.ok) {
            showSourcesStatus(formatError(payload), "error");
            return;
        }

        applyComparisonResult(payload);
        showSourcesStatus("Veri kaynağı mutabakatı tamamlandı.", "success");
        await loadHistory();
    } catch {
        showSourcesStatus(getNetworkErrorMessage(), "error");
    } finally {
        databaseCompareButton.textContent = "Veri kaynaklarını karşılaştır";
        updateDatabaseCompareButton();
    }
}

async function queueDatabaseSourcesComparison() {
    hideAlert();
    if (!areDatabaseSourcesReady()) {
        showSourcesStatus("İki veri kaynağı da aktif ve hazır olmalıdır.", "error");
        return;
    }

    databaseQueueButton.disabled = true;
    databaseQueueButton.textContent = "Kuyruga aliniyor";
    try {
        const response = await fetch("/api/reconciliations/compare-database-sources/jobs", {
            method: "POST",
            headers: createInitiatorHeaders()
        });
        const payload = await response.json();
        if (!response.ok) {
            showSourcesStatus(formatError(payload), "error");
            return;
        }

        selectedBatchId = payload.batchId;
        selectedBatch = {
            batchId: payload.batchId,
            batchStatus: payload.status,
            approvalStatus: "NotApplicable"
        };
        setWorkflowStep(4, "active");
        updateSelectedBatchInfo(selectedBatch);
        showSourcesStatus("Mutabakat kuyruğa alındı.", "success");
        await loadHistory();
        await monitorBackgroundBatch(payload.batchId);
    } catch (error) {
        showSourcesStatus(error.message || getNetworkErrorMessage(), "error");
    } finally {
        databaseQueueButton.textContent = "Arka planda çalıştır";
        updateDatabaseCompareButton();
    }
}

async function monitorBackgroundBatch(batchId, reportStatus = showSourcesStatus) {
    for (let attempt = 0; attempt < 120; attempt++) {
        const response = await fetch(`/api/reconciliations/${batchId}`);
        const payload = await response.json();
        if (!response.ok) {
            throw new Error(formatError(payload));
        }

        if (payload.status === "Completed") {
            await loadBatch(batchId);
            await loadHistory();
            setWorkflowStep(4, "complete");
            reportStatus("Arka plan mutabakatı tamamlandı.", "success");
            return;
        }
        if (payload.status === "Failed") {
            await loadBatch(batchId);
            await loadHistory();
            setWorkflowStep(4, "error");
            reportStatus(payload.errorMessage ?? "Arka plan mutabakatı tamamlanamadı.", "error");
            return;
        }

        reportStatus(`Mutabakat durumu: ${payload.status}`, "success");
        await new Promise(resolve => setTimeout(resolve, 1000));
    }

    reportStatus("Mutabakat arka planda devam ediyor. Geçmiş listesinden izlenebilir.", "success");
}

function createSourceField(label, field, value, multiline = false) {
    const wrapper = document.createElement("label");
    wrapper.className = "settings-field";
    const caption = document.createElement("span");
    caption.textContent = label;
    const control = document.createElement(multiline ? "textarea" : "input");
    control.dataset.sourceField = field;
    control.value = value ?? "";
    if (multiline) {
        control.rows = 2;
    }
    wrapper.append(caption, control);
    return wrapper;
}

async function saveSource(sourceId) {
    const item = sourcesList.querySelector(`[data-source-id="${sourceId}"]`);
    const button = item?.querySelector("[data-source-save]");
    if (!item || !button) {
        return;
    }

    if (!requireManagementAccess(showSourcesStatus)) {
        return;
    }

    button.disabled = true;
    try {
        const response = await fetch(`/api/reconciliation-sources/${sourceId}`, {
            method: "PUT",
            headers: createManagementHeaders(),
            body: JSON.stringify({
                displayName: item.querySelector('[data-source-field="displayName"]').value.trim(),
                description: item.querySelector('[data-source-field="description"]').value.trim(),
                isActive: item.querySelector('[data-source-field="isActive"]').checked
            })
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
            showSourcesStatus(formatManagementError(response.status, payload), "error");
            return;
        }

        const sourceIndex = currentSources.findIndex(source => source.id === sourceId);
        if (sourceIndex >= 0) {
            currentSources[sourceIndex] = payload;
        }
        updateDatabaseCompareButton();
        showSourcesStatus(`${payload.displayName} güncellendi.`, "success");
        await loadAuditEvents(true);
    } catch {
        showSourcesStatus(getNetworkErrorMessage(), "error");
    } finally {
        button.disabled = false;
    }
}

function showSourcesStatus(message, status) {
    sourcesStatus.textContent = message;
    sourcesStatus.className = `validation-status validation-status-${status}`;
    sourcesStatus.hidden = false;
}

function renderComparisonSettings(settings) {
    currentComparisonSettings = settings;
    populateComparisonFieldSelectors(settings);
    setChecked("normalize-code-case", settings.normalizeCodeCase);
    setChecked("trim-text-values", settings.trimTextValues);
    setValue("require-exact-match", String(Boolean(settings.requireExactMatch)));
    setValue("quantity-tolerance", settings.quantityTolerance ?? 0);
    setValue("amount-tolerance", settings.amountTolerance ?? 0);
    setValue("trim-branch-code", nullableBooleanValue(settings.trimBranchCode));
    setValue("trim-fund-code", nullableBooleanValue(settings.trimFundCode));
    setValue("trim-transaction-number", nullableBooleanValue(settings.trimTransactionNumber));
    setValue("quantity-decimal-places", settings.quantityDecimalPlaces);
    setValue("branch-quantity-decimal-places", settings.branchQuantityDecimalPlaces);
    setValue("bank-quantity-decimal-places", settings.bankQuantityDecimalPlaces);
    setValue("amount-decimal-places", settings.amountDecimalPlaces);
    setValue("branch-amount-decimal-places", settings.branchAmountDecimalPlaces);
    setValue("bank-amount-decimal-places", settings.bankAmountDecimalPlaces);
    setMultiSelectValues("matching-fields", settings.matchingFields ?? []);
    setMultiSelectValues("comparison-fields", settings.comparisonFields ?? []);
    setMultiSelectValues("result-fields", settings.resultFields ?? []);
    renderMappingRows("branchCodeMappings", settings.branchCodeMappings);
    renderMappingRows("fundCodeMappings", settings.fundCodeMappings);
    renderMappingRows("transactionNumberMappings", settings.transactionNumberMappings);
    renderFieldMappingRows(settings.fieldMappings);
    if (selectedBatch) renderSummary(selectedBatch);
    setAdministratorControls(currentUser?.role === "Administrator");
}

function populateComparisonFieldSelectors(settings = null) {
    const fields = currentSchemaColumns.map(column => ({ value: column.field, label: column.name || column.field }));
    for (const id of ["matching-fields", "comparison-fields", "result-fields"]) {
        const select = document.querySelector(`#${id}`);
        const selected = settings
            ? settings[id === "matching-fields" ? "matchingFields" : id === "comparison-fields" ? "comparisonFields" : "resultFields"] ?? []
            : [...select.selectedOptions].map(option => option.value);
        select.replaceChildren();
        for (const field of fields) {
            const option = document.createElement("option");
            option.value = field.value;
            option.textContent = `${field.label} (${field.value})`;
            option.selected = selected.includes(field.value);
            select.append(option);
        }
    }
}

function setMultiSelectValues(id, values) {
    const select = document.querySelector(`#${id}`);
    for (const option of select.options) option.selected = values.includes(option.value);
}

function renderMappingRows(editorName, mappings = {}) {
    const rows = document.querySelector(`[data-mapping-editor="${editorName}"] .mapping-rows`);
    rows.replaceChildren();
    for (const [source, target] of Object.entries(mappings ?? {})) addMappingRow(editorName, null, source, target);
}

function renderFieldMappingRows(fieldMappings = {}) {
    const rows = document.querySelector('[data-mapping-editor="fieldMappings"] .mapping-rows');
    rows.replaceChildren();
    for (const [field, mappings] of Object.entries(fieldMappings ?? {})) {
        for (const [source, target] of Object.entries(mappings)) addMappingRow("fieldMappings", field, source, target);
    }
}

function addMappingRow(editorName, field = null, source = "", target = "") {
    const rows = document.querySelector(`[data-mapping-editor="${editorName}"] .mapping-rows`);
    const row = document.createElement("div");
    row.className = "mapping-row";
    if (editorName === "fieldMappings") {
        const select = document.createElement("select");
        select.dataset.mappingField = "true";
        for (const column of currentSchemaColumns) {
            const option = document.createElement("option");
            option.value = column.field;
            option.textContent = column.name || column.field;
            option.selected = column.field === field;
            select.append(option);
        }
        row.append(select);
    }
    const sourceInput = document.createElement("input");
    sourceInput.placeholder = "Dosyadaki değer";
    sourceInput.value = source;
    sourceInput.dataset.mappingSource = "true";
    const arrow = document.createElement("span");
    arrow.textContent = "→";
    const targetInput = document.createElement("input");
    targetInput.placeholder = "Karşılaştırılacak değer";
    targetInput.value = target;
    targetInput.dataset.mappingTarget = "true";
    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "danger mapping-remove";
    remove.dataset.removeMapping = "true";
    remove.textContent = "Sil";
    row.append(sourceInput, arrow, targetInput, remove);
    rows.append(row);
}

async function saveComparisonSettings() {
    if (!requireManagementAccess(showComparisonSettingsStatus)) {
        return;
    }

    comparisonSettingsSaveButton.disabled = true;
    comparisonSettingsSaveButton.textContent = "Uygulaniyor";
    hideComparisonSettingsStatus();

    try {
        const response = await fetch("/api/reconciliation-comparison-settings", {
            method: "PUT",
            headers: createManagementHeaders(),
            body: JSON.stringify(collectComparisonSettings())
        });
        const payload = await response.json().catch(() => ({}));

        if (!response.ok) {
            showComparisonSettingsStatus(formatManagementError(response.status, payload), "error");
            return;
        }

        renderComparisonSettings(payload);
        showComparisonSettingsStatus("Karşılaştırma ayarları güncellendi ve kalıcı olarak kaydedildi.", "success");
        await loadAuditEvents(true);
    } catch (error) {
        showComparisonSettingsStatus(error.message || getNetworkErrorMessage(), "error");
    } finally {
        comparisonSettingsSaveButton.disabled = false;
        comparisonSettingsSaveButton.textContent = "Ayarlari uygula";
    }
}

function collectComparisonSettings() {
    return {
        normalizeCodeCase: getChecked("normalize-code-case"),
        trimTextValues: getChecked("trim-text-values"),
        requireExactMatch: getValue("require-exact-match") === "true",
        quantityTolerance: Number(getValue("quantity-tolerance") || 0),
        amountTolerance: Number(getValue("amount-tolerance") || 0),
        trimBranchCode: getNullableBoolean("trim-branch-code"),
        trimFundCode: getNullableBoolean("trim-fund-code"),
        trimTransactionNumber: getNullableBoolean("trim-transaction-number"),
        quantityDecimalPlaces: getNullableNumber("quantity-decimal-places"),
        branchQuantityDecimalPlaces: getNullableNumber("branch-quantity-decimal-places"),
        bankQuantityDecimalPlaces: getNullableNumber("bank-quantity-decimal-places"),
        amountDecimalPlaces: getNullableNumber("amount-decimal-places"),
        branchAmountDecimalPlaces: getNullableNumber("branch-amount-decimal-places"),
        bankAmountDecimalPlaces: getNullableNumber("bank-amount-decimal-places"),
        matchingFields: getListValue("matching-fields"),
        comparisonFields: getListValue("comparison-fields"),
        resultFields: getListValue("result-fields"),
        branchCodeMappings: collectMappingRows("branchCodeMappings"),
        fundCodeMappings: collectMappingRows("fundCodeMappings"),
        transactionNumberMappings: collectMappingRows("transactionNumberMappings"),
        fieldMappings: collectFieldMappingRows()
    };
}

function collectMappingRows(editorName) {
    return [...document.querySelectorAll(`[data-mapping-editor="${editorName}"] .mapping-row`)]
        .reduce((mappings, row) => {
            const source = row.querySelector("[data-mapping-source]").value.trim();
            const target = row.querySelector("[data-mapping-target]").value.trim();
            if (!source || !target) throw new Error("Eşleme satırlarında iki değer de doldurulmalıdır.");
            mappings[source] = target;
            return mappings;
        }, {});
}

function collectFieldMappingRows() {
    return [...document.querySelectorAll('[data-mapping-editor="fieldMappings"] .mapping-row')]
        .reduce((fieldMappings, row) => {
            const field = row.querySelector("[data-mapping-field]").value;
            const source = row.querySelector("[data-mapping-source]").value.trim();
            const target = row.querySelector("[data-mapping-target]").value.trim();
            if (!field || !source || !target) throw new Error("Alan eşlemesindeki tüm seçimleri doldurun.");
            fieldMappings[field] ??= {};
            fieldMappings[field][source] = target;
            return fieldMappings;
        }, {});
}

function parseMappings(value) {
    return value.split("\n").reduce((mappings, line) => {
        if (!line.trim()) {
            return mappings;
        }
        const separator = line.indexOf("=");
        const source = separator < 0 ? "" : line.slice(0, separator).trim();
        const target = separator < 0 ? "" : line.slice(separator + 1).trim();
        if (!source || !target) {
            throw new Error("Esleme satirlarini Kaynak=Hedef formatinda girin.");
        }
        mappings[source] = target;
        return mappings;
    }, {});
}

function parseFieldMappings(value) {
    return value.split("\n").reduce((fieldMappings, line) => {
        const fieldSeparator = line.indexOf("|");
        if (!line.trim()) {
            return fieldMappings;
        }
        if (fieldSeparator <= 0) {
            throw new Error("Genel alan eslemelerini Alan|Kaynak=Hedef formatinda girin.");
        }
        const field = line.slice(0, fieldSeparator).trim();
        const mappings = parseMappings(line.slice(fieldSeparator + 1));
        fieldMappings[field] = { ...(fieldMappings[field] ?? {}), ...mappings };
        return fieldMappings;
    }, {});
}

function formatMappings(mappings = {}) {
    return Object.entries(mappings).map(([source, target]) => `${source}=${target}`).join("\n");
}

function formatFieldMappings(fieldMappings = {}) {
    return Object.entries(fieldMappings).flatMap(([field, mappings]) =>
        Object.entries(mappings).map(([source, target]) => `${field}|${source}=${target}`)).join("\n");
}

function getListValue(id) {
    const element = document.querySelector(`#${id}`);
    if (element instanceof HTMLSelectElement && element.multiple) {
        return [...element.selectedOptions].map(option => option.value);
    }
    return getValue(id).split(",").map(value => value.trim()).filter(Boolean);
}

function getValue(id) {
    return document.querySelector(`#${id}`)?.value?.trim() ?? "";
}

function setValue(id, value) {
    const element = document.querySelector(`#${id}`);
    if (element) {
        element.value = value ?? "";
    }
}

function getChecked(id) {
    return Boolean(document.querySelector(`#${id}`)?.checked);
}

function setChecked(id, value) {
    const element = document.querySelector(`#${id}`);
    if (element) {
        element.checked = Boolean(value);
    }
}

function getNullableNumber(id) {
    const value = getValue(id);
    return value === "" ? null : Number(value);
}

function getNullableBoolean(id) {
    const value = getValue(id);
    return value === "" ? null : value === "true";
}

function nullableBooleanValue(value) {
    return value === null || value === undefined ? "" : String(value);
}

function showComparisonSettingsStatus(message, status) {
    comparisonSettingsStatus.textContent = message;
    comparisonSettingsStatus.className = `validation-status validation-status-${status}`;
    comparisonSettingsStatus.hidden = false;
}

function hideComparisonSettingsStatus() {
    comparisonSettingsStatus.hidden = true;
    comparisonSettingsStatus.textContent = "";
    comparisonSettingsStatus.className = "validation-status";
}

function renderFileSchema(columns = []) {
    currentSchemaColumns = columns.map((column, index) => ({ ...column, position: index + 1 }));
    schemaList.replaceChildren();

    if (columns.length === 0) {
        const empty = document.createElement("p");
        empty.className = "empty-state schema-empty";
        empty.textContent = "Dosya şeması yüklenemedi.";
        schemaList.append(empty);
        return;
    }

    currentSchemaColumns.forEach((column, index) => {
        const item = document.createElement("div");
        item.className = "schema-item";
        item.dataset.field = column.field;
        item.dataset.schemaIndex = index;

        const heading = document.createElement("div");
        heading.className = "schema-item-heading";
        const title = document.createElement("strong");
        title.textContent = `${index + 1}. ${column.field}`;
        const actions = document.createElement("div");
        actions.className = "schema-item-actions";
        actions.append(
            createSchemaActionButton("up", "↑", index, index === 0, "Yukarı taşı"),
            createSchemaActionButton("down", "↓", index, index === currentSchemaColumns.length - 1, "Aşağı taşı"),
            createSchemaActionButton("remove", "Sil", index, requiredCoreFields.has(column.field), "Kolonu sil")
        );
        heading.append(title, actions);

        const meta = document.createElement("span");
        meta.textContent = [
            column.type,
            column.required ? "required" : "optional",
            column.dateFormat ? `format ${column.dateFormat}` : null,
            column.patternDescription ? column.patternDescription : null,
            column.minLength ? `min ${column.minLength}` : null,
            column.maxLength ? `max ${column.maxLength}` : null,
            column.minValue !== null && column.minValue !== undefined ? `>= ${column.minValue}` : null,
            column.maxValue !== null && column.maxValue !== undefined ? `<= ${column.maxValue}` : null,
            column.maxDecimalPlaces !== null && column.maxDecimalPlaces !== undefined
                ? `scale ${column.maxDecimalPlaces}`
                : null,
            column.fixedWidthStart !== null && column.fixedWidthStart !== undefined
                ? `fixed ${column.fixedWidthStart}:${column.fixedWidthLength}`
                : null,
            column.allowedValues?.length ? `values ${column.allowedValues.join(", ")}` : null
        ].filter(Boolean).join(" | ");

        const description = document.createElement("span");
        description.className = "schema-description";
        description.textContent = column.description ?? "";

        const fields = document.createElement("div");
        fields.className = "schema-editor";
        fields.append(
            createSchemaInput(column, "field", "Alan adı", "all", requiredCoreFields.has(column.field)),
            createSchemaInput(column, "name", "Header"),
            createSchemaTypeSelect(column),
            createSchemaInput(column, "dateFormat", "Tarih biçimi", "Date"),
            createSchemaInput(column, "pattern", "Metin deseni (Regex)", "Text"),
            createSchemaInput(column, "patternDescription", "Desen açıklaması", "Text"),
            createSchemaInput(column, "minLength", "Minimum uzunluk", "Text"),
            createSchemaInput(column, "maxLength", "Maksimum uzunluk", "Text"),
            createSchemaInput(column, "minValue", "Minimum değer", "Decimal,Integer"),
            createSchemaInput(column, "maxValue", "Maksimum değer", "Decimal,Integer"),
            createSchemaInput(column, "maxDecimalPlaces", "Ondalık basamak", "Decimal"),
            createSchemaInput(column, "fixedWidthStart", "Fixed start"),
            createSchemaInput(column, "fixedWidthLength", "Fixed length"),
            createSchemaInput(
                { ...column, allowedValues: (column.allowedValues ?? []).join(", ") },
                "allowedValues",
                "İzin verilen değerler",
                "Text"),
            createSchemaRequiredToggle(column)
        );

        item.append(heading, meta, description, fields);
        schemaList.append(item);
        updateSchemaTypeVisibility(item);
    });
    populateComparisonFieldSelectors(currentComparisonSettings);
    setAdministratorControls(currentUser?.role === "Administrator");
}

function createSchemaActionButton(action, text, index, disabled, label) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = action === "remove" ? "danger schema-action" : "secondary schema-action";
    button.dataset.schemaAction = action;
    button.dataset.schemaIndex = index;
    button.textContent = text;
    button.disabled = disabled;
    button.title = label;
    button.setAttribute("aria-label", label);
    return button;
}

function createSchemaInput(column, key, label, appliesTo = "all", readOnly = false) {
    const wrapper = document.createElement("label");
    wrapper.className = "schema-field";
    wrapper.dataset.appliesTo = appliesTo;

    const caption = document.createElement("span");
    caption.textContent = label;

    const input = document.createElement("input");
    input.value = column[key] ?? "";
    input.dataset.field = column.field;
    input.dataset.schemaKey = key;
    input.readOnly = readOnly;

    wrapper.append(caption, input);
    return wrapper;
}

function createSchemaTypeSelect(column) {
    const wrapper = document.createElement("label");
    wrapper.className = "schema-field";

    const caption = document.createElement("span");
    caption.textContent = "Type";

    const select = document.createElement("select");
    select.dataset.field = column.field;
    select.dataset.schemaKey = "type";

    for (const type of ["Text", "Date", "Decimal", "Integer"]) {
        const option = document.createElement("option");
        option.value = type;
        option.textContent = type;
        option.selected = column.type === type;
        select.append(option);
    }

    wrapper.append(caption, select);
    return wrapper;
}

function updateSchemaTypeVisibility(item) {
    const selectedType = getSchemaControlValue(item, "type");
    for (const field of item.querySelectorAll("[data-applies-to]")) {
        const appliesTo = field.dataset.appliesTo;
        field.hidden = appliesTo !== "all" && !appliesTo.split(",").includes(selectedType);
    }
}

function createSchemaRequiredToggle(column) {
    const wrapper = document.createElement("label");
    wrapper.className = "schema-check";

    const input = document.createElement("input");
    input.type = "checkbox";
    input.checked = Boolean(column.required);
    input.dataset.field = column.field;
    input.dataset.schemaKey = "required";

    const caption = document.createElement("span");
    caption.textContent = "Required";

    wrapper.append(input, caption);
    return wrapper;
}

async function saveFileSchema() {
    hideAlert();
    hideSchemaStatus();

    if (currentSchemaColumns.length === 0) {
        showSchemaStatus("Güncellenecek şema bulunamadı.", "error");
        return;
    }

    if (!requireManagementAccess(showSchemaStatus)) {
        return;
    }

    const columns = collectSchemaColumns();
    schemaSaveButton.disabled = true;
    schemaSaveButton.textContent = "Uygulaniyor";

    try {
        const response = await fetch("/api/reconciliation-file-schema", {
            method: "PUT",
            headers: createManagementHeaders(),
            body: JSON.stringify({ columns })
        });
        const payload = await response.json().catch(() => ({}));

        if (!response.ok) {
            showSchemaStatus(formatManagementError(response.status, payload), "error");
            return;
        }

        renderFileSchema(payload);
        await loadComparisonSettings();
        showSchemaStatus("Şema güncellendi. Yeni zorunlu kolonlar validasyonda, yeni sayısal kolonlar karşılaştırmada kontrol edilecek.", "success");
        await loadAuditEvents(true);
    } catch {
        showSchemaStatus(getNetworkErrorMessage(), "error");
    } finally {
        schemaSaveButton.disabled = false;
        schemaSaveButton.textContent = "Şemayı uygula";
    }
}

function collectSchemaColumns() {
    return currentSchemaColumns.map(column => {
        const item = schemaList.querySelector(`[data-field="${column.field}"]`);

        return {
            field: getSchemaControlValue(item, "field") || column.field,
            name: getSchemaControlValue(item, "name"),
            type: getSchemaControlValue(item, "type"),
            required: getSchemaControlChecked(item, "required"),
            dateFormat: getSchemaControlValue(item, "dateFormat") || null,
            pattern: getSchemaControlValue(item, "pattern") || null,
            patternDescription: getSchemaControlValue(item, "patternDescription") || null,
            minLength: getSchemaControlNumber(item, "minLength"),
            maxLength: getSchemaControlNumber(item, "maxLength"),
            minValue: getSchemaControlNumber(item, "minValue"),
            maxValue: getSchemaControlNumber(item, "maxValue"),
            maxDecimalPlaces: getSchemaControlNumber(item, "maxDecimalPlaces"),
            fixedWidthStart: getSchemaControlNumber(item, "fixedWidthStart"),
            fixedWidthLength: getSchemaControlNumber(item, "fixedWidthLength"),
            allowedValues: getSchemaControlList(item, "allowedValues"),
            description: column.description ?? ""
        };
    });
}

function getSchemaControlValue(item, key) {
    return item?.querySelector(`[data-schema-key="${key}"]`)?.value?.trim() ?? "";
}

function getSchemaControlChecked(item, key) {
    return Boolean(item?.querySelector(`[data-schema-key="${key}"]`)?.checked);
}

function getSchemaControlNumber(item, key) {
    const value = getSchemaControlValue(item, key);
    return value === "" ? null : Number(value);
}

function getSchemaControlList(item, key) {
    return getSchemaControlValue(item, key)
        .split(",")
        .map(value => value.trim())
        .filter(Boolean);
}

function showSchemaStatus(message, status) {
    schemaStatus.textContent = message;
    schemaStatus.className = `validation-status validation-status-${status}`;
    schemaStatus.hidden = false;
}

function hideSchemaStatus() {
    schemaStatus.hidden = true;
    schemaStatus.textContent = "";
    schemaStatus.className = "validation-status";
}

async function loadBatch(batchId) {
    hideAlert();

    try {
        const response = await fetch(`/api/reconciliations/${batchId}`);
        const payload = await response.json();

        if (!response.ok) {
            showAlert(formatError(payload));
            return;
        }

        selectedBatchId = payload.id;
        selectedBatch = payload;
        renderSummary(payload);
        setResults(payload.results ?? []);
        updateSelectedBatchInfo(payload);
        renderSelectedHistoryItem();
        if (payload.status === "Completed") setWorkflowStep(4, "complete");
        if (payload.status === "Failed") setWorkflowStep(4, "error");
        if (payload.approvalStatus === "Approved" || payload.approvalStatus === "Rejected") {
            setWorkflowStep(5, "complete");
        }
    } catch {
        showAlert(getNetworkErrorMessage());
    }
}

function renderHistory(history = []) {
    historyList.replaceChildren();

    if (history.length === 0) {
        const empty = document.createElement("p");
        empty.className = "empty-state history-empty";
        empty.textContent = "Geçmiş mutabakat kaydı yok.";
        historyList.append(empty);
        return;
    }

    for (const item of history) {
        const row = document.createElement("div");
        row.className = "history-row";
        const button = document.createElement("button");
        button.type = "button";
        button.className = "history-item";
        button.dataset.batchId = item.id;
        button.append(
            createHistoryTitle(item),
            createHistoryMeta(item),
            createHistoryCounts(item)
        );
        row.append(button);
        if (item.status === "Completed") {
            const download = document.createElement("a");
            download.className = "history-export-link";
            download.href = `/api/reconciliations/${item.id}/export`;
            download.textContent = "Excel indir";
            download.setAttribute("download", "");
            download.setAttribute("aria-label", `${formatDateTime(item.createdAt)} sonucunu Excel olarak indir`);
            row.append(download);
        }
        historyList.append(row);
    }

    renderSelectedHistoryItem();
}

function createHistoryTitle(item) {
    const title = document.createElement("strong");
    title.textContent = formatDateTime(item.createdAt);
    return title;
}

function createHistoryMeta(item) {
    const meta = document.createElement("span");
    meta.className = "history-meta";
    const initiator = item.initiatedBy ? ` | Başlatan: ${item.initiatedBy}` : "";
    meta.textContent = `${formatBatchStatus(item.status)} | Onay: ${formatApprovalStatus(item.approvalStatus)}${initiator} | ${item.branchFileName} / ${item.bankFileName}`;
    return meta;
}

function createHistoryCounts(item) {
    const counts = document.createElement("span");
    counts.className = "history-counts";
    const attempt = item.attemptCount > 0 ? `Deneme ${item.attemptCount} | ` : "";
    if (item.status === "Failed") {
        counts.classList.add("history-error");
        counts.textContent = `${attempt}${item.errorCode ?? "Hata"}: ${item.errorMessage ?? "İşlem tamamlanamadı."}`;
        return counts;
    }
    if (item.status === "Queued" || item.status === "Processing") {
        const nextAttempt = item.nextAttemptAt
            ? ` Sonraki deneme: ${formatDateTime(item.nextAttemptAt)}.`
            : "";
        counts.textContent = item.status === "Queued"
            ? `${attempt}İş kuyrukta bekliyor.${nextAttempt}`
            : `${attempt}Mutabakat işleniyor.`;
        return counts;
    }

    counts.textContent = `Eşleşen ${item.matchedCount} | Farklı ${item.mismatchCount} | Yalnızca Dosya 1: ${item.onlyInBranchCount} | Yalnızca Dosya 2: ${item.onlyInBankCount}`;
    return counts;
}

function renderSelectedHistoryItem() {
    for (const item of historyList.querySelectorAll("[data-batch-id]")) {
        item.classList.toggle("selected", item.dataset.batchId === selectedBatchId);
    }
}

function updateSelectedBatchInfo(summary = {}) {
    if (!selectedBatchId) {
        selectedBatch = null;
        exportButton.disabled = true;
        selectedBatchInfo.textContent = "Henüz mutabakat seçilmedi.";
        updateApprovalPanel();
        return;
    }

    selectedBatch = summary;

    const createdAt = summary.createdAt ? formatDateTime(summary.createdAt) : "";
    const branchFileName = summary.branchFileName ?? "";
    const bankFileName = summary.bankFileName ?? "";
    const status = summary.status ?? summary.batchStatus ?? "";
    exportButton.disabled = status !== "Completed";
    const error = summary.errorCode
        ? ` | ${summary.errorCode}: ${summary.errorMessage ?? "İşlem tamamlanamadı."}`
        : "";
    const attempt = summary.attemptCount > 0 ? ` | Deneme ${summary.attemptCount}` : "";
    const nextAttempt = summary.nextAttemptAt
        ? ` | Sonraki deneme ${formatDateTime(summary.nextAttemptAt)}`
        : "";
    const initiator = summary.initiatedBy ? ` | Başlatan: ${summary.initiatedBy}` : "";
    selectedBatchInfo.textContent = `${createdAt} ${formatBatchStatus(status)} ${branchFileName} / ${bankFileName}${initiator}${attempt}${nextAttempt}${error}`.trim();
    updateApprovalPanel(summary);
}

function updateApprovalPanel(batch = selectedBatch) {
    const status = batch?.status ?? batch?.batchStatus ?? "";
    const currentApprovalStatus = batch?.approvalStatus ?? "NotApplicable";
    approvalStatus.className = `approval-badge ${currentApprovalStatus}`;
    approvalStatus.textContent = formatApprovalStatus(currentApprovalStatus);

    if (!selectedBatchId) {
        approvalDecisionMeta.textContent = "Onay için tamamlanmış bir mutabakat seçin.";
    } else if (batch?.decisionBy) {
        const decidedAt = batch.decisionAt ? `, ${formatDateTime(batch.decisionAt)}` : "";
        const note = batch.decisionComment ? ` | ${batch.decisionComment}` : "";
        approvalDecisionMeta.textContent = `${batch.decisionBy}${decidedAt}${note}`;
    } else if (status === "Completed" && currentApprovalStatus === "Pending") {
        approvalDecisionMeta.textContent = "Yetkili kullanıcının kararı bekleniyor.";
    } else {
        approvalDecisionMeta.textContent = "Onay kararı yalnızca tamamlanmış mutabakatlar için verilebilir.";
    }

    updateApprovalActions();
}

function updateApprovalActions() {
    const status = selectedBatch?.status ?? selectedBatch?.batchStatus ?? "";
    const canDecide = Boolean(selectedBatchId) &&
        status === "Completed" &&
        selectedBatch?.approvalStatus === "Pending" &&
        currentUser?.role === "Approver" &&
        Boolean(accessToken);
    approveButton.disabled = !canDecide;
    rejectButton.disabled = !canDecide;
}

async function submitApproval(decision) {
    hideApprovalFeedback();
    const comment = approvalComment.value.trim();

    if (!selectedBatchId || currentUser?.role !== "Approver" || !accessToken) {
        showApprovalFeedback("Tamamlanmış bir mutabakat ve Approver oturumu gereklidir.", "error");
        return;
    }

    if (decision === "Reject" && !comment) {
        showApprovalFeedback("Reddetme gerekcesi zorunludur.", "error");
        return;
    }

    approveButton.disabled = true;
    rejectButton.disabled = true;
    try {
        const response = await fetch(`/api/reconciliations/${selectedBatchId}/approval`, {
            method: "POST",
            headers: createAuthorizationHeaders(true),
            body: JSON.stringify({ decision, comment: comment || null })
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
            throw new Error(formatApprovalError(response.status, payload));
        }

        selectedBatch = payload;
        selectedBatchId = payload.id;
        updateSelectedBatchInfo(payload);
        approvalComment.value = "";
        showApprovalFeedback(
            decision === "Approve" ? "Mutabakat onaylandı." : "Mutabakat reddedildi.",
            "success");
        setWorkflowStep(5, "complete");
        await loadHistory();
        await loadAuditEvents(true);
    } catch (error) {
        showApprovalFeedback(error.message || getNetworkErrorMessage(), "error");
        updateApprovalActions();
    }
}

function formatApprovalError(status, payload) {
    if (status === 401) {
        return "Oturum geçersiz veya süresi dolmuş. Yeniden giriş yapın.";
    }
    if (status === 403) {
        return "Bu kullanicinin onay yetkisi yok.";
    }
    return formatError(payload);
}

function formatApprovalStatus(status) {
    switch (status) {
        case "Pending":
            return "Onay bekliyor";
        case "Approved":
            return "Onaylandı";
        case "Rejected":
            return "Reddedildi";
        default:
            return "Uygulanamaz";
    }
}

function formatBatchStatus(status) {
    switch (status) {
        case "Completed":
            return "Tamamlandı";
        case "Failed":
            return "Hatalı";
        case "Queued":
            return "Kuyrukta";
        case "Processing":
            return "İşleniyor";
        default:
            return status ?? "";
    }
}

function showApprovalFeedback(message, status) {
    approvalFeedback.textContent = message;
    approvalFeedback.className = `validation-status validation-status-${status}`;
    approvalFeedback.hidden = false;
}

function hideApprovalFeedback() {
    approvalFeedback.hidden = true;
    approvalFeedback.textContent = "";
    approvalFeedback.className = "validation-status";
}

function requireManagementAccess(showStatus) {
    if (accessToken && currentUser?.role === "Administrator") {
        return true;
    }

    showStatus("Bu işlem için Admin hesabıyla giriş yapın.", "error");
    return false;
}

function createManagementHeaders() {
    return createAuthorizationHeaders(true);
}

function formatManagementError(status, payload) {
    if (status === 401) {
        return "Oturum geçersiz veya süresi dolmuş. Yeniden giriş yapın.";
    }
    if (status === 403) {
        return "Bu kullanicinin yonetim yetkisi yok.";
    }
    return formatError(payload);
}

async function loadAuditEvents(silent = false) {
    if (!accessToken || currentUser?.role !== "Administrator") {
        auditRetentionStatus.hidden = true;
        if (!silent) {
            showAuditStatus("İşlem kayıtları için Admin hesabıyla giriş yapın.", "error");
        }
        return;
    }

    const retentionStatusPromise = loadAuditRetentionStatus();

    const query = new URLSearchParams({ take: "50" });
    if (auditActor.value.trim()) {
        query.set("actor", auditActor.value.trim());
    }
    if (auditAction.value) {
        query.set("action", auditAction.value);
    }
    if (auditResourceType.value) {
        query.set("resourceType", auditResourceType.value);
    }

    auditRefreshButton.disabled = true;
    try {
        const response = await fetch(`/api/reconciliation-audit-events?${query}`, {
            headers: createAuthorizationHeaders()
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
            showAuditStatus(formatManagementError(response.status, payload), "error");
            return;
        }

        renderAuditEvents(payload);
        const totalCount = response.headers.get("X-Total-Count") ?? payload.length;
        showAuditStatus(`${payload.length} / ${totalCount} islem kaydi gosteriliyor.`, "success");
    } catch {
        showAuditStatus(getNetworkErrorMessage(), "error");
    } finally {
        await retentionStatusPromise;
        auditRefreshButton.disabled = false;
    }
}

async function loadAuditRetentionStatus() {
    try {
        const response = await fetch("/api/reconciliation-audit-retention/status", {
            headers: createAuthorizationHeaders()
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
            auditRetentionStatus.hidden = true;
            return;
        }

        renderAuditRetentionStatus(payload);
    } catch {
        auditRetentionStatus.hidden = true;
    }
}

function renderAuditRetentionStatus(status) {
    const labels = {
        Ready: "Arsiv duzenli calisiyor",
        Backlog: "Degistirilemez arsive aktarim bekliyor",
        Degraded: "Arsiv islemi dikkat gerektiriyor",
        Disabled: "Otomatik arsiv kapali"
    };
    const statusName = labels[status.status] ?? "Arsiv durumu bilinmiyor";
    const lastSuccessfulRun = status.lastSucceededAt
        ? formatDateTime(status.lastSucceededAt)
        : "Henuz tamamlanmadi";
    const pendingSummary = status.immutableArchiveEnabled
        ? ` | WORM bekleyen: ${status.pendingExternalArchiveCount}`
        : "";
    const alertLabels = {
        LastRunFailed: "son calisma basarisiz",
        RunOverdue: "beklenen calisma gecikti",
        ExternalArchiveBacklogCount: "WORM kuyrugu sayi esigini asti",
        ExternalArchiveBacklogAge: "WORM kuyrugu yas esigini asti"
    };
    const alertSummary = status.alerts?.length
        ? ` | Uyari: ${status.alerts.map(alert => alertLabels[alert] ?? alert).join(", ")}`
        : "";

    auditRetentionStatus.replaceChildren();
    const heading = document.createElement("strong");
    heading.textContent = statusName;
    const details = document.createElement("span");
    details.textContent = `Aktif kayit: ${status.hotEventCount} | Arsiv kaydi: ${status.archivedEventCount}${pendingSummary} | Son basarili calisma: ${lastSuccessfulRun}${alertSummary}`;
    auditRetentionStatus.append(heading, details);
    auditRetentionStatus.className = `audit-retention-status audit-retention-status-${String(status.status).toLowerCase()}`;
    auditRetentionStatus.hidden = false;
}

function renderAuditEvents(events = []) {
    auditList.replaceChildren();
    if (events.length === 0) {
        const empty = document.createElement("p");
        empty.className = "empty-state audit-empty";
        empty.textContent = "Filtreye uygun islem kaydi yok.";
        auditList.append(empty);
        return;
    }

    for (const event of events) {
        const item = document.createElement("article");
        item.className = "audit-item";

        const titleRow = document.createElement("div");
        titleRow.className = "audit-title-row";
        const title = document.createElement("strong");
        title.textContent = formatAuditAction(event.action);
        const date = document.createElement("span");
        date.className = "audit-meta";
        date.textContent = formatDateTime(event.createdAt);
        titleRow.append(title, date);

        const meta = document.createElement("span");
        meta.className = "audit-meta";
        const archiveStatus = event.archivedAt
            ? ` | Arsiv: ${event.integrityVerified ? "butunluk dogrulandi" : "butunluk hatasi"}`
            : "";
        const immutableStatus = event.externalArchivedAt ? " | WORM: aktarildi" : "";
        meta.textContent = `${event.actor} | ${formatAuditResource(event.resourceType)} | ${event.resourceId}${archiveStatus}${immutableStatus}`;
        item.append(titleRow, meta);

        if (event.beforeState || event.afterState) {
            item.append(createAuditDetails(event));
        }
        auditList.append(item);
    }
}

function createAuditDetails(event) {
    const details = document.createElement("details");
    details.className = "audit-details";
    const summary = document.createElement("summary");
    summary.textContent = "Degisiklikleri goster";
    const grid = document.createElement("div");
    grid.className = "audit-state-grid";
    grid.append(
        createAuditState("Once", event.beforeState),
        createAuditState("Sonra", event.afterState)
    );
    details.append(summary, grid);
    return details;
}

function createAuditState(label, state) {
    const wrapper = document.createElement("div");
    const title = document.createElement("strong");
    title.textContent = label;
    const content = document.createElement("pre");
    content.textContent = state ? JSON.stringify(state, null, 2) : "Kayit yok";
    wrapper.append(title, content);
    return wrapper;
}

function formatAuditAction(action) {
    switch (action) {
        case "ReconciliationApproved":
            return "Mutabakat onaylandi";
        case "ReconciliationRejected":
            return "Mutabakat reddedildi";
        case "UserRegistered":
            return "Kullanıcı kaydı oluşturuldu";
        case "UserRoleUpdated":
            return "Kullanıcı rolü güncellendi";
        case "SourceUpdated":
            return "Veri kaynagi guncellendi";
        case "FileSchemaUpdated":
            return "Dosya semasi guncellendi";
        case "ComparisonSettingsUpdated":
            return "Karsilastirma ayarlari guncellendi";
        default:
            return action ?? "Islem";
    }
}

function formatAuditResource(resourceType) {
    switch (resourceType) {
        case "ReconciliationBatch":
            return "Mutabakat";
        case "UserAccount":
            return "Kullanıcı hesabı";
        case "ReconciliationSource":
            return "Veri kaynagi";
        case "FileSchema":
            return "Dosya semasi";
        case "ComparisonSettings":
            return "Karsilastirma ayarlari";
        default:
            return resourceType ?? "Kaynak";
    }
}

function showAuditStatus(message, status) {
    auditStatus.textContent = message;
    auditStatus.className = `validation-status validation-status-${status}`;
    auditStatus.hidden = false;
}

function setResults(results) {
    currentResults = results;
    renderFilteredResults();
}

function renderFilteredResults() {
    renderResults(filterResults(currentResults, statusFilter.value));
}

function filterResults(results, filter) {
    if (filter === "All") {
        return results;
    }

    if (filter === "Mismatch") {
        return results.filter(result => isMismatchStatus(result.status));
    }

    return results.filter(result => result.status === filter);
}

function isMismatchStatus(status) {
    return status === "QuantityMismatch" ||
        status === "AmountMismatch" ||
        status === "QuantityAndAmountMismatch" ||
        status === "FieldMismatch";
}

function renderResults(results) {
    const resultFields = getResultFields(currentResults);
    const differenceFields = getDifferenceFields(currentResults);
    renderResultHeader(resultFields, differenceFields);
    counters.resultCount.textContent = `${results.length} / ${currentResults.length} kayıt`;
    resultsBody.replaceChildren();

    if (results.length === 0) {
        const row = document.createElement("tr");
        const cell = document.createElement("td");
        cell.colSpan = resultFields.length + differenceFields.length + 3;
        cell.className = "empty-state";
        cell.textContent = currentResults.length === 0
            ? "Karşılaştırma sonucu burada görünecek."
            : "Seçili filtreye uygun kayıt yok.";
        row.append(cell);
        resultsBody.append(row);
        return;
    }

    for (const result of results) {
        const row = document.createElement("tr");
        row.className = getResultRowClass(result.status);
        row.append(
            createStatusCell(result.status),
            ...resultFields.map(field => createTextCell(getResultFieldValue(result, field))),
            createTextCell(formatNumber(result.quantityDifference)),
            createTextCell(formatNumber(result.amountDifference)),
            ...differenceFields.map(field => createTextCell(formatNumber(result.fieldDifferences?.[field])))
        );
        resultsBody.append(row);
    }
}

function renderResultHeader(resultFields, differenceFields) {
    resultsHead.replaceChildren(
        createHeaderCell("Durum"),
        ...resultFields.map(field => createHeaderCell(formatFieldName(field))),
        createHeaderCell("Adet farkı"),
        createHeaderCell("Tutar farkı"),
        ...differenceFields.map(field => createHeaderCell(`${formatFieldName(field)} farkı`))
    );
}

function createHeaderCell(text) {
    const cell = document.createElement("th");
    cell.textContent = text;
    return cell;
}

function getResultFields(results) {
    const fields = [];

    for (const result of results) {
        for (const field of Object.keys(result.fieldValues ?? {})) {
            if (!fields.includes(field)) {
                fields.push(field);
            }
        }
    }

    return fields.length > 0 ? fields : defaultResultFields;
}

function getDifferenceFields(results) {
    const fields = [];

    for (const result of results) {
        for (const field of Object.keys(result.fieldDifferences ?? {})) {
            if (!fields.includes(field)) {
                fields.push(field);
            }
        }
    }

    return fields;
}

function getResultFieldValue(result, field) {
    if (result.fieldValues && Object.prototype.hasOwnProperty.call(result.fieldValues, field)) {
        return result.fieldValues[field];
    }

    const fallbackKey = field.charAt(0).toLowerCase() + field.slice(1);
    return result[fallbackKey] ?? "";
}

function getResultRowClass(status) {
    if (isMismatchStatus(status)) {
        return "result-row result-row-mismatch";
    }

    switch (status) {
        case "Matched":
            return "result-row result-row-matched";
        case "OnlyInBranch":
            return "result-row result-row-branch-only";
        case "OnlyInBank":
            return "result-row result-row-bank-only";
        default:
            return "result-row";
    }
}

function createStatusCell(status) {
    const cell = document.createElement("td");
    cell.className = "status-cell";

    const badge = document.createElement("span");
    badge.className = `status ${status}`;
    badge.textContent = formatResultStatus(status);

    const description = document.createElement("span");
    description.className = "status-description";
    description.textContent = getStatusDescription(status);

    cell.append(badge, description);
    return cell;
}

function getStatusDescription(status) {
    switch (status) {
        case "Matched":
            return "Iki tarafta da var, adet ve tutar ayni.";
        case "QuantityMismatch":
            return "Iki tarafta da var, adet farkli.";
        case "AmountMismatch":
            return "Iki tarafta da var, tutar farkli.";
        case "QuantityAndAmountMismatch":
            return "Iki tarafta da var, adet ve tutar farkli.";
        case "FieldMismatch":
            return "Iki tarafta da var, ek karsilastirma alanlarinda fark var.";
        case "OnlyInBranch":
            return "Yalnızca Karşılaştırma Dosyası 1'de var.";
        case "OnlyInBank":
            return "Yalnızca Karşılaştırma Dosyası 2'de var.";
        default:
            return "Durum aciklamasi yok.";
    }
}

function formatResultStatus(status) {
    switch (status) {
        case "Matched": return "Eşleşen";
        case "QuantityMismatch": return "Adet farklı";
        case "AmountMismatch": return "Tutar farklı";
        case "QuantityAndAmountMismatch": return "Adet ve tutar farklı";
        case "FieldMismatch": return "Alan farklı";
        case "OnlyInBranch": return "Yalnızca Dosya 1";
        case "OnlyInBank": return "Yalnızca Dosya 2";
        default: return status ?? "";
    }
}

function formatFieldName(field) {
    const labels = {
        BranchCode: "Kaynak kodu",
        FundCode: "Fon kodu",
        TransactionNumber: "İşlem numarası",
        TransactionDate: "İşlem tarihi",
        Quantity: "Adet",
        Amount: "Tutar"
    };
    return labels[field] ?? field;
}

function createTextCell(value) {
    const cell = document.createElement("td");
    cell.textContent = value ?? "";
    return cell;
}

function formatNumber(value) {
    if (value === null || value === undefined) {
        return "";
    }

    return Number(value).toLocaleString("tr-TR");
}

function formatDateTime(value) {
    if (!value) {
        return "";
    }

    return new Date(value).toLocaleString("tr-TR");
}

renderSummary();
renderFilteredResults();
updateSelectedBatchInfo();
updateAuthenticationUi();
restoreAuthenticationSession();
loadFileSchema();
loadComparisonSettings();
loadSources();
loadHistory();
loadRuntimeSettings();
window.setInterval(refreshActiveBackgroundJobs, 3000);
