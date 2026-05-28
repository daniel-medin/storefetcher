// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

(() => {
  const grid = document.querySelector("[data-store-grid]");
  if (!grid) {
    return;
  }

  const searchForm = document.querySelector("[data-store-grid-search]");
  const queryInput = document.querySelector("[data-store-grid-query]");
  const body = grid.querySelector("[data-store-grid-body]");
  const status = grid.querySelector("[data-store-grid-status]");
  const pageInfo = grid.querySelector("[data-store-grid-page-info]");
  const pageSizeSelect = grid.querySelector("[data-store-grid-page-size]");
  const sortButtons = [...grid.querySelectorAll("[data-sort]")];
  const pageButtons = [...grid.querySelectorAll("[data-page-action]")];
  const editBaseUrl = grid.dataset.editBaseUrl || "/Admin/Stores/Edit";
  const urlState = new URLSearchParams(window.location.search);

  const state = {
    q: urlState.get("q") || grid.dataset.initialQuery || "",
    page: positiveInt(urlState.get("page"), 1),
    pageSize: positiveInt(urlState.get("pageSize"), Number(grid.dataset.pageSize) || 100),
    sort: urlState.get("sort") || "name",
    dir: urlState.get("dir") === "desc" ? "desc" : "asc",
    total: 0,
  };

  if (queryInput) {
    queryInput.value = state.q;
  }
  if (pageSizeSelect) {
    pageSizeSelect.value = String(state.pageSize);
  }

  let debounceTimer = null;
  let requestVersion = 0;

  searchForm?.addEventListener("submit", (event) => {
    event.preventDefault();
    state.q = queryInput?.value.trim() || "";
    state.page = 1;
    loadStores();
  });

  queryInput?.addEventListener("input", () => {
    window.clearTimeout(debounceTimer);
    debounceTimer = window.setTimeout(() => {
      state.q = queryInput.value.trim();
      state.page = 1;
      loadStores();
    }, 300);
  });

  pageSizeSelect?.addEventListener("change", () => {
    state.pageSize = positiveInt(pageSizeSelect.value, 100);
    state.page = 1;
    loadStores();
  });

  for (const button of sortButtons) {
    button.dataset.label = button.textContent.trim();
    button.addEventListener("click", () => {
      const nextSort = button.dataset.sort;
      if (state.sort === nextSort) {
        state.dir = state.dir === "asc" ? "desc" : "asc";
      } else {
        state.sort = nextSort;
        state.dir = "asc";
      }
      state.page = 1;
      loadStores();
    });
  }

  for (const button of pageButtons) {
    button.addEventListener("click", () => {
      const totalPages = pageCount();
      switch (button.dataset.pageAction) {
        case "first":
          state.page = 1;
          break;
        case "prev":
          state.page = Math.max(1, state.page - 1);
          break;
        case "next":
          state.page = Math.min(totalPages, state.page + 1);
          break;
        case "last":
          state.page = totalPages;
          break;
      }
      loadStores();
    });
  }

  loadStores();

  async function loadStores() {
    const version = ++requestVersion;
    setLoading(true);
    updateSortHeaders();
    syncUrl();

    try {
      const response = await fetch(`/api/stores?${storeQueryString()}`, {
        headers: { accept: "application/json" },
      });
      if (!response.ok) {
        throw new Error(`${response.status} ${response.statusText}`);
      }

      const data = await response.json();
      if (version !== requestVersion) {
        return;
      }

      state.total = data.total || 0;
      const totalPages = pageCount();
      if (state.total > 0 && state.page > totalPages) {
        state.page = totalPages;
        await loadStores();
        return;
      }

      renderRows(data.stores || []);
      renderPager();
      setLoading(false);
    } catch (error) {
      if (version !== requestVersion) {
        return;
      }

      body.replaceChildren(rowMessage(`Could not load stores: ${error.message}`));
      status.textContent = "Store list failed to load";
      setLoading(false);
    }
  }

  function storeQueryString() {
    const params = new URLSearchParams({
      page: String(state.page),
      pageSize: String(state.pageSize),
      sort: state.sort,
      dir: state.dir,
    });
    if (state.q) {
      params.set("q", state.q);
    }
    return params.toString();
  }

  function renderRows(stores) {
    if (stores.length === 0) {
      body.replaceChildren(rowMessage("No stores found."));
      return;
    }

    body.replaceChildren(...stores.map(renderStoreRow));
  }

  function renderStoreRow(store) {
    const row = document.createElement("tr");
    row.append(
      nameCell(store),
      textCell(store.brand || ""),
      textCell(formatAddress(store)),
      textCell(formatCoordinates(store)),
      textCell(store.hasCorrection ? "Yes" : "No"),
      textCell(formatDate(store.updatedAt)),
      actionCell(store),
    );
    return row;
  }

  function nameCell(store) {
    const cell = document.createElement("td");
    const name = document.createElement("div");
    name.className = "fw-semibold";
    name.textContent = store.name || "";
    cell.append(name);

    const osmLabel = `${store.osmType || ""}/${store.osmId || ""}`;
    if (store.osmUrl?.startsWith("http")) {
      const link = document.createElement("a");
      link.href = store.osmUrl;
      link.target = "_blank";
      link.rel = "noreferrer";
      link.textContent = osmLabel;
      cell.append(link);
    } else {
      const source = document.createElement("div");
      source.className = "text-muted small";
      source.textContent = osmLabel;
      cell.append(source);
    }

    return cell;
  }

  function actionCell(store) {
    const cell = document.createElement("td");
    cell.className = "text-end";
    const link = document.createElement("a");
    link.className = "btn btn-sm btn-outline-primary";
    link.href = `${editBaseUrl}/${store.id}`;
    link.textContent = "Edit";
    cell.append(link);
    return cell;
  }

  function textCell(value) {
    const cell = document.createElement("td");
    cell.textContent = value;
    return cell;
  }

  function rowMessage(message) {
    const row = document.createElement("tr");
    const cell = document.createElement("td");
    cell.colSpan = 7;
    cell.className = "text-muted py-3";
    cell.textContent = message;
    row.append(cell);
    return row;
  }

  function formatAddress(store) {
    const street = [store.street, store.houseNumber].filter(Boolean).join(" ");
    const parts = [street, store.postcode, store.city].filter(Boolean);
    return parts.length > 0 ? parts.join(", ") : "Missing address";
  }

  function formatCoordinates(store) {
    if (!Number.isFinite(store.latitude) || !Number.isFinite(store.longitude)) {
      return "";
    }
    return `${store.latitude.toFixed(6)}, ${store.longitude.toFixed(6)}`;
  }

  function formatDate(value) {
    if (!value) {
      return "";
    }
    return new Intl.DateTimeFormat("sv-SE", {
      dateStyle: "short",
      timeStyle: "short",
    }).format(new Date(value));
  }

  function renderPager() {
    const totalPages = pageCount();
    const firstItem = state.total === 0 ? 0 : (state.page - 1) * state.pageSize + 1;
    const lastItem = Math.min(state.total, state.page * state.pageSize);

    status.textContent = `${firstItem}-${lastItem} of ${state.total} stores`;
    pageInfo.textContent = `Page ${state.page} of ${totalPages}`;

    for (const button of pageButtons) {
      const action = button.dataset.pageAction;
      button.disabled =
        state.total === 0 ||
        ((action === "first" || action === "prev") && state.page <= 1) ||
        ((action === "next" || action === "last") && state.page >= totalPages);
    }
  }

  function updateSortHeaders() {
    for (const button of sortButtons) {
      const sorted = button.dataset.sort === state.sort;
      button.classList.toggle("table-sort-active", sorted);
      button.setAttribute(
        "aria-sort",
        sorted ? (state.dir === "asc" ? "ascending" : "descending") : "none",
      );
      button.textContent = `${button.dataset.label}${sorted ? (state.dir === "asc" ? " ↑" : " ↓") : ""}`;
    }
  }

  function setLoading(loading) {
    grid.classList.toggle("store-grid-loading", loading);
    if (loading) {
      status.textContent = "Loading stores...";
    }
  }

  function pageCount() {
    return Math.max(1, Math.ceil(state.total / state.pageSize));
  }

  function syncUrl() {
    const params = new URLSearchParams();
    if (state.q) {
      params.set("q", state.q);
    }
    if (state.page > 1) {
      params.set("page", String(state.page));
    }
    if (state.pageSize !== 100) {
      params.set("pageSize", String(state.pageSize));
    }
    if (state.sort !== "name") {
      params.set("sort", state.sort);
    }
    if (state.dir !== "asc") {
      params.set("dir", state.dir);
    }

    const next = `${window.location.pathname}${params.size ? `?${params}` : ""}`;
    window.history.replaceState(null, "", next);
  }

  function positiveInt(value, fallback) {
    const parsed = Number.parseInt(value, 10);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
  }
})();

(() => {
  const status = document.querySelector("[data-address-status]");
  if (!status) {
    return;
  }

  const statusMessage = document.querySelector("[data-address-status-message]");
  const statusPid = document.querySelector("[data-address-status-pid]");
  const runButton = document.querySelector("[data-address-run-button]");
  const importButton = document.querySelector("[data-address-import-button]");
  const enrichedSummary = document.querySelector("[data-address-enriched-summary]");
  const reviewSummary = document.querySelector("[data-address-review-summary]");

  let polling = status.dataset.running === "true";
  if (polling) {
    pollAddressStatus();
  } else {
    refreshAddressStatus();
  }

  async function pollAddressStatus() {
    await refreshAddressStatus();
    if (polling) {
      window.setTimeout(pollAddressStatus, 2500);
    }
  }

  async function refreshAddressStatus() {
    try {
      const response = await fetch("/api/admin/address-enrichment/status", {
        headers: { accept: "application/json" },
      });
      if (!response.ok) {
        return;
      }

      const data = await response.json();
      polling = Boolean(data.running);
      status.classList.toggle("address-status-running", polling);
      status.dataset.running = String(polling);
      statusMessage.textContent = data.message || "";
      statusPid.textContent = data.processId ? `PID ${data.processId}` : "";

      if (runButton) {
        runButton.disabled = polling;
        runButton.textContent = polling ? "Searching..." : "Find addresses";
      }

      if (enrichedSummary) {
        enrichedSummary.textContent = formatAddressSummary(data.enrichedImport);
      }
      if (reviewSummary) {
        reviewSummary.textContent = formatAddressSummary(data.review);
      }
      if (importButton) {
        importButton.disabled = polling || !data.enrichedImport?.exists;
      }
    } catch {
      // The page can keep working with the last known status.
    }
  }

  function formatAddressSummary(summary) {
    if (!summary?.exists) {
      return "No file yet";
    }

    const count = Number.isInteger(summary.count)
      ? `${summary.count} records`
      : "unknown count";
    const timestamp = summary.generatedAt || summary.lastWriteTime;
    return timestamp ? `${count}, ${formatSummaryDate(timestamp)}` : count;
  }

  function formatSummaryDate(value) {
    return new Intl.DateTimeFormat("sv-SE", {
      dateStyle: "short",
      timeStyle: "short",
    }).format(new Date(value));
  }
})();
