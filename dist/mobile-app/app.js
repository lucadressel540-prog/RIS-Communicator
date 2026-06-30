const screens = {
  splash: document.getElementById("splash"),
  home: document.getElementById("home"),
  searchMenu: document.getElementById("searchMenuPage"),
  search: document.getElementById("searchPage"),
  stationSearch: document.getElementById("stationSearchPage"),
  preview: document.getElementById("previewPage"),
  stationBoard: document.getElementById("stationBoardPage"),
  journey: document.getElementById("journeyPage"),
  safety: document.getElementById("safetyPage"),
  impressum: document.getElementById("impressumPage"),
  settings: document.getElementById("settingsPage"),
  notifications: document.getElementById("notificationsPage"),
  profile: document.getElementById("profilePage"),
  appearance: document.getElementById("appearancePage"),
  risImport: document.getElementById("risImportPage"),
  profileSetup: document.getElementById("profileSetupPage"),
  editor: document.getElementById("editorPage")
};

const searchForm = document.getElementById("searchForm");
const trainSearch = document.getElementById("trainSearch");
const searchResult = document.getElementById("searchResult");
const clearButton = document.querySelector("[data-action='clear-search']");
const stationSearchForm = document.getElementById("stationSearchForm");
const stationSearch = document.getElementById("stationSearch");
const stationSearchResult = document.getElementById("stationSearchResult");
const stationOptions = document.getElementById("stationOptions");
const stationClearButton = document.querySelector("[data-action='clear-station-search']");
const previewTitle = document.getElementById("previewTitle");
const previewRoute = document.getElementById("previewRoute");
const stationBoardTitle = document.getElementById("stationBoardTitle");
const stationBoardDateButton = document.getElementById("stationBoardDateButton");
const stationBoardDateInput = document.getElementById("stationBoardDateInput");
const stationBoardTime = document.getElementById("stationBoardTime");
const stationBoardTimeInput = document.getElementById("stationBoardTimeInput");
const stationBoardMode = document.getElementById("stationBoardMode");
const stationBoardList = document.getElementById("stationBoardList");
const roleSheet = document.getElementById("roleSheet");
const appMenu = document.getElementById("appMenu");
const updateDialog = document.getElementById("updateDialog");
const resetLoginDialog = document.getElementById("resetLoginDialog");
const roleText = document.getElementById("roleText");
const routeScroll = document.getElementById("routeScroll");
const journeyTitle = document.getElementById("journeyTitle");
const checkoutButton = document.getElementById("checkoutButton");
const editorForm = document.getElementById("editorForm");
const editNumber = document.getElementById("editNumber");
const editLine = document.getElementById("editLine");
const editFrom = document.getElementById("editFrom");
const editTo = document.getElementById("editTo");
const editStops = document.getElementById("editStops");
const editorDownloadLink = document.getElementById("editorDownloadLink");
const profileSetupForm = document.getElementById("profileSetupForm");
const setupName = document.getElementById("setupName");
const setupPhone = document.getElementById("setupPhone");
const risImportForm = document.getElementById("risImportForm");
const risUsername = document.getElementById("risUsername");
const risPassword = document.getElementById("risPassword");
const risImportStatus = document.getElementById("risImportStatus");
const profileNameDisplay = document.getElementById("profileNameDisplay");
const profilePhoneDisplay = document.getElementById("profilePhoneDisplay");
const firstReminderValue = document.getElementById("firstReminderValue");
const laterReminderValue = document.getElementById("laterReminderValue");

installImageFallbacks();

const defaultTrain = {
  number: "4865",
  line: "RE2",
  from: "Hof Hbf",
  to: "M\u00fcnchen Hbf",
  duration: "3 h 42 min",
  stops: [
    ["Hof Hbf", "", "14:34", "6a"],
    ["Marktredwitz", "15:01", "15:01", "5"],
    ["Weiden(Oberpf)", "15:41", "15:44", "2"],
    ["Schwandorf", "16:09", "16:10", "4"],
    ["Regensburg Hbf", "16:38", "16:46", "1"],
    ["Eggm\u00fchl", "17:00", "17:01", "2"],
    ["Neufahrn(Niederbay)", "17:11", "17:12", "1"],
    ["Ergoldsbach", "17:15", "17:16", "2"],
    ["Landshut(Bay)Hbf", "17:28", "17:30", "6"],
    ["Moosburg", "17:40", "17:41", "2"],
    ["Freising", "17:50", "17:51", "2"],
    ["M\u00fcnchen Hbf", "18:16", "", "31"]
  ]
};

const dateGroups = buildDateGroups();
const DATA_VERSION_KEY = "ris-data-version";
const ACTIVE_BOOKING_KEY = "ris-active-booking";

let trains = loadTrains();
let stations = [];
let recentBookings = loadRecentBookings();
let currentTrain = trains[0];
let currentPreviewTrain = trains[0];
let searchMode = "booking";
let stationSearchMode = "board";
let selectedStation = null;
let stationBoardKind = "departure";
let stationBoardDate = new Date();
let stationBoardStartMinutes = 183;
let selectedDate = "today";
let lastScreen = "home";
let previewReturnScreen = "searchMenu";
let incidentAnswer = "";
let pendingAppVersion = "";
let profile = loadProfile();
let preferences = loadPreferences();
let dataUpdateInProgress = false;
let stationSearchTimer = 0;
let editorDownloadUrl = "";

function buildDateGroups() {
  const now = new Date();
  return [
    { label: `Gestern ${formatShortDate(addDays(now, -1))}`, key: "yesterday" },
    { label: `Heute ${formatShortDate(now)}`, key: "today" },
    { label: `Morgen ${formatShortDate(addDays(now, 1))}`, key: "tomorrow" }
  ];
}

function addDays(date, days) {
  const copy = new Date(date);
  copy.setDate(copy.getDate() + days);
  return copy;
}

function formatShortDate(date) {
  return new Intl.DateTimeFormat("de-DE", {
    day: "2-digit",
    month: "2-digit",
    year: "2-digit"
  }).format(date);
}

function formatIsoDate(date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function formatBoardDate(date) {
  const today = formatIsoDate(new Date());
  const selected = formatIsoDate(date);
  if (selected === today) return "Heute";
  return formatShortDate(date);
}

function formatClock(minutes) {
  const normalized = ((minutes % 1440) + 1440) % 1440;
  const hours = Math.floor(normalized / 60);
  const mins = normalized % 60;
  return `${String(hours).padStart(2, "0")}:${String(mins).padStart(2, "0")}`;
}

function loadTrains() {
  const saved = window.localStorage.getItem("ris-trains");
  if (!saved) return [defaultTrain];
  try {
    const parsed = JSON.parse(saved);
    return Array.isArray(parsed) && parsed.length ? parsed : [defaultTrain];
  } catch {
    return [defaultTrain];
  }
}

async function loadAppData() {
  try {
    let response = await fetch(`api/trains?v=${Date.now()}`, { cache: "no-store" });
    if (!response.ok) {
      response = await fetch(`data/trains.json?v=${Date.now()}`, { cache: "no-store" });
    }
    if (!response.ok) return;

    const parsed = await response.json();
    const remoteTrains = Array.isArray(parsed) ? parsed : parsed?.trains;
    if (!Array.isArray(remoteTrains) || !remoteTrains.length) return;

    trains = remoteTrains
      .map(normalizeTrain)
      .filter((train) => train.number && train.stops.length >= 2);

    if (trains.length) {
      currentTrain = trains.find((train) => train.number === currentTrain?.number) || trains[0];
      currentPreviewTrain = trains.find((train) => train.number === currentPreviewTrain?.number) || currentTrain;
      const version = String(parsed?.version || "").trim();
      if (version) window.localStorage.setItem(DATA_VERSION_KEY, version);
      saveTrains();
    }
  } catch {
    // Offline/file mode keeps the local editor data or default demo train.
  }

  await loadStations();
}

async function loadStations() {
  try {
    const response = await fetch(`data/stations.json?v=${Date.now()}`, { cache: "no-store" });
    if (!response.ok) return;

    const parsed = await response.json();
    if (!Array.isArray(parsed)) return;

    stations = parsed
      .filter((station) => station?.name && station?.ril100)
      .map((station) => ({
        name: String(station.name).trim(),
        ril100: String(station.ril100).trim(),
        eva: String(station.eva || "").trim()
      }));
    renderStationOptions();
  } catch {
    stations = [];
  }
}

function normalizeTrain(train) {
  const stops = Array.isArray(train.stops)
    ? train.stops.map((stop) => [
      String(stop?.[0] || ""),
      String(stop?.[1] || ""),
      String(stop?.[2] || ""),
      String(stop?.[3] || "")
    ]).filter((stop) => stop[0])
    : [];

  return {
    number: String(train.number || "").trim(),
    line: String(train.line || "").trim() || "RE",
    from: String(train.from || stops[0]?.[0] || "").trim(),
    to: String(train.to || stops[stops.length - 1]?.[0] || "").trim(),
    duration: String(train.duration || estimateDuration(stops) || "").trim(),
    stops
  };
}

function saveTrains() {
  window.localStorage.setItem("ris-trains", JSON.stringify(trains));
}

function loadRecentBookings() {
  const saved = window.localStorage.getItem("ris-recent-bookings");
  if (!saved) return [];
  try {
    const parsed = JSON.parse(saved);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function saveRecentBookings() {
  window.localStorage.setItem("ris-recent-bookings", JSON.stringify(recentBookings.slice(0, 8)));
}

function loadProfile() {
  const saved = window.localStorage.getItem("ris-user-profile");
  if (!saved) return null;
  try {
    const parsed = JSON.parse(saved);
    if (!parsed || !parsed.name || !parsed.phone) return null;
    return {
      name: String(parsed.name),
      phone: String(parsed.phone)
    };
  } catch {
    return null;
  }
}

function saveProfile(profileData) {
  profile = profileData;
  window.localStorage.setItem("ris-user-profile", JSON.stringify(profileData));
}

function loadPreferences() {
  const defaults = {
    firstReminder: 6,
    laterReminder: 2,
    customerFeedback: true,
    trafficControl: true,
    disruptions: true,
    information: true,
    userInformation: true,
    showOwnName: true,
    theme: "light"
  };

  const saved = window.localStorage.getItem("ris-user-preferences");
  if (!saved) return defaults;

  try {
    return { ...defaults, ...JSON.parse(saved) };
  } catch {
    return defaults;
  }
}

function savePreferences() {
  window.localStorage.setItem("ris-user-preferences", JSON.stringify(preferences));
}

function showScreen(name) {
  Object.values(screens).forEach((screen) => screen.classList.add("hidden"));
  screens[name].classList.remove("hidden");
  lastScreen = name === "editor" ? lastScreen : name;
}

function openSearch(mode = "booking") {
  searchMode = mode;
  trainSearch.value = "";
  trainSearch.closest(".search-field").classList.remove("has-value");
  showScreen("search");
  renderSearchResult();
}

function openSearchMenu() {
  showScreen("searchMenu");
}

function renderSearchResult() {
  const value = trainSearch.value.trim();
  trainSearch.closest(".search-field").classList.toggle("has-value", value.length > 0);
  screens.search.classList.remove("recent-template");
  clearButton.classList.toggle("hidden", value.length === 0);

  if (!value) {
    renderRecentBookings();
    return;
  }

  const train = trains.find((item) => item.number === value);
  if (!train) {
    searchResult.innerHTML = `
      <div class="empty-state">
        ${emptyTrainIcon()}
        <p>F&uuml;r deine Sucheingabe &bdquo;${escapeHtml(value)}&ldquo; haben wir keinen<br>passenden Zug gefunden</p>
      </div>
    `;
    return;
  }

  currentTrain = train;
  searchResult.innerHTML = `
    <div class="date-list">
      ${dateGroups.map((group) => renderDateGroup(group, group.key === selectedDate)).join("")}
    </div>
  `;
}

function renderRecentBookings() {
  if (!recentBookings.length) {
    screens.search.classList.remove("recent-template");
    searchResult.innerHTML = "";
    return;
  }

  screens.search.classList.add("recent-template");
  const grouped = recentBookings.reduce((result, booking) => {
    if (!result[booking.dayLabel]) result[booking.dayLabel] = [];
    result[booking.dayLabel].push(booking);
    return result;
  }, {});

  searchResult.innerHTML = `
    <section class="recent-list">
      <h2>Zuletzt ge&ouml;ffnet</h2>
      ${Object.entries(grouped).map(([label, bookings]) => `
        <div class="recent-group">
          <p>${escapeHtml(label)}</p>
          ${bookings.map((booking) => `
            <button class="train-badge recent-badge" type="button" data-recent-number="${escapeHtml(booking.number)}" data-recent-date="${escapeHtml(booking.dateKey)}">
              ${trainSvg()} ${escapeHtml(booking.line)} (${escapeHtml(booking.number)})
            </button>
          `).join("")}
        </div>
      `).join("")}
    </section>
  `;
}

function renderDateGroup(group, open) {
  return `
    <section class="date-group">
      <button class="date-title" type="button" data-date="${group.key}">
        <span>${group.label}</span>
        <span>${open ? "&#8963;" : "&#8964;"}</span>
      </button>
      ${open ? renderTrainCard(currentTrain) : ""}
    </section>
  `;
}

function renderTrainCard(train) {
  const first = train.stops[0];
  const last = train.stops[train.stops.length - 1];
  return `
    <button class="result-card" type="button" data-action="${searchMode === "preview" ? "open-train-preview" : "open-roles"}">
      <div class="card-times">
        <b>${first[2] || first[1]}</b>
        <em>${first[2] || first[1]}</em>
        <span>${escapeHtml(train.duration)}</span>
        <b>${last[1] || last[2]}</b>
      </div>
      <div class="mini-route" aria-hidden="true">
        <span class="ring"></span>
        <span class="line"></span>
        <span class="pin"></span>
      </div>
      <div class="card-places">
        <b>${escapeHtml(train.from)}</b>
        <span class="train-badge">${trainSvg()} ${escapeHtml(train.line)} (${escapeHtml(train.number)})</span>
        <b>${escapeHtml(train.to)}</b>
      </div>
    </button>
  `;
}

function renderStationOptions() {
  if (!stationOptions || !stations.length) return;
  const usedNames = new Set(trains.flatMap((train) => train.stops.map((stop) => normalizeText(stop[0]))));
  const relevant = stations
    .filter((station) => usedNames.has(normalizeText(station.name)))
    .slice(0, 160);

  stationOptions.innerHTML = relevant
    .map((station) => `<option value="${escapeHtml(station.name)}">${escapeHtml(station.ril100)}</option>`)
    .join("");
}

function openStationSearch(mode = "board") {
  stationSearchMode = mode;
  stationSearch.value = "";
  stationSearch.closest(".search-field").classList.remove("has-value");
  stationClearButton.classList.add("hidden");
  showScreen("stationSearch");
  renderStationSearchResult();
}

function renderStationSearchResult() {
  const value = stationSearch.value.trim();
  stationSearch.closest(".search-field").classList.toggle("has-value", value.length > 0);
  stationClearButton.classList.toggle("hidden", value.length === 0);

  const matches = findStations(value).slice(0, 14);
  if (!value) {
    stationSearchResult.innerHTML = "";
    return;
  }

  if (!matches.length) {
    stationSearchResult.innerHTML = `<div class="station-empty">Kein Bahnhof f&uuml;r &bdquo;${escapeHtml(value)}&ldquo; gefunden</div>`;
    return;
  }

  stationSearchResult.innerHTML = `
    <div class="station-suggestions">
      ${matches.map((station) => `
        <button type="button" data-station-ril100="${escapeHtml(station.ril100)}">
          <b>${escapeHtml(station.name)}</b>
          <span>${escapeHtml(station.ril100)}${station.eva ? ` &middot; EVA ${escapeHtml(station.eva)}` : ""}</span>
        </button>
      `).join("")}
    </div>
  `;
}

function findStations(query) {
  const normalized = normalizeText(query);
  if (!normalized) return [];

  const byTrainStops = uniqueStationsFromTrains()
    .map((station) => ({ ...station, score: stationScore(station, normalized) }))
    .filter((station) => station.score < 99);

  const byOriginalData = stations
    .map((station) => ({ ...station, score: stationScore(station, normalized) + 2 }))
    .filter((station) => station.score < 99);

  const merged = new Map();
  [...byTrainStops, ...byOriginalData].forEach((station) => {
    const key = station.ril100 || normalizeText(station.name);
    const existing = merged.get(key);
    if (!existing || station.score < existing.score) merged.set(key, station);
  });

  return [...merged.values()].sort((a, b) => a.score - b.score || a.name.localeCompare(b.name, "de"));
}

function uniqueStationsFromTrains() {
  const result = new Map();
  trains.forEach((train) => {
    train.stops.forEach((stop) => {
      const name = stop[0];
      const station = findStationByName(name) || { name, ril100: fallbackRil100(name), eva: "" };
      result.set(normalizeText(name), station);
    });
  });
  return [...result.values()];
}

function stationScore(station, query) {
  const name = normalizeText(station.name);
  const ril100 = normalizeText(station.ril100);
  if (ril100 === query) return 0;
  if (name === query) return 1;
  if (ril100.startsWith(query)) return 2;
  if (name.startsWith(query)) return 3;
  if (name.includes(query)) return 5;
  return 99;
}

function findStationByName(name) {
  const normalized = normalizeText(name);
  return stations.find((station) => stationMatchesName(station, normalized)) || null;
}

function findStationByCodeOrName(value) {
  const normalized = normalizeText(value);
  const exact = stations.find((station) =>
    normalizeText(station.ril100) === normalized
    || stationMatchesName(station, normalized)
  );
  if (exact) return exact;

  const matches = findStations(value);
  return matches[0] || null;
}

function stationMatchesStop(station, stop) {
  const stopName = String(stop?.[0] || "");
  if (!stopName) return false;

  const normalizedStopName = normalizeText(stopName);
  const stopStation = findStationByName(stopName);
  return stationMatchesName(station, normalizedStopName)
    || normalizeText(station.ril100) === normalizeText(stopStation?.ril100 || "");
}

function stationMatchesName(station, normalizedName) {
  return normalizeStationName(station.name) === normalizeStationName(normalizedName)
    || normalizeText(station.name) === normalizedName;
}

function normalizeStationName(value) {
  return normalizeText(value)
    .replace(/HAUPTBAHNHOF/g, "HBF")
    .replace(/BAHNHOF/g, "BF")
    .replace(/[^A-Z0-9]/g, "");
}

function fallbackRil100(name) {
  return normalizeText(name).replace(/[^A-Z0-9]/g, "").slice(0, 4) || "---";
}

function openRoleSheet() {
  roleText.innerHTML = `<span class="role-line">Bitte w&auml;hle deine Rolle aus, um dich in den <b>${escapeHtml(currentTrain.line)}</b></span><span class="role-line"><b>(${escapeHtml(currentTrain.number)})</b> einzubuchen</span>`;
  roleSheet.classList.remove("hidden");
}

function closeRoleSheet() {
  roleSheet.classList.add("hidden");
}

function openAppMenu() {
  appMenu.classList.remove("hidden");
}

function closeAppMenu() {
  appMenu.classList.add("hidden");
}

function openImpressum() {
  closeAppMenu();
  showScreen("impressum");
}

function openSettings() {
  closeAppMenu();
  renderPreferences();
  showScreen("settings");
}

function requestResetLogin() {
  resetLoginDialog.classList.remove("hidden");
}

function cancelResetLogin() {
  resetLoginDialog.classList.add("hidden");
}

function resetLogin() {
  resetLoginDialog.classList.add("hidden");
  window.localStorage.removeItem("ris-user-profile");
  profile = null;
  setupName.value = "";
  setupPhone.value = "";
  showScreen("profileSetup");
}

function openProfile() {
  renderProfile();
  showScreen("profile");
}

function openAppearance() {
  renderAppearance();
  showScreen("appearance");
}

function openNotifications() {
  renderPreferences();
  showScreen("notifications");
}

function openRisImport() {
  risImportStatus.textContent = "";
  risPassword.value = "";
  showScreen("risImport");
}

function renderProfile() {
  profileNameDisplay.textContent = profile?.name || "-";
  profilePhoneDisplay.textContent = profile?.phone || "-";
}

function renderPreferences() {
  firstReminderValue.textContent = preferences.firstReminder;
  laterReminderValue.textContent = preferences.laterReminder;
  document.querySelectorAll("[data-toggle]").forEach((button) => {
    button.classList.toggle("active", Boolean(preferences[button.dataset.toggle]));
  });
}

function renderAppearance() {
  document.querySelectorAll("[data-theme]").forEach((button) => {
    button.classList.toggle("selected", button.dataset.theme === preferences.theme);
    const existing = button.querySelector("span");
    if (existing) existing.remove();
    if (button.dataset.theme === preferences.theme) {
      const mark = document.createElement("span");
      mark.textContent = "\u2713";
      button.appendChild(mark);
    }
  });
}

function saveProfileSetup() {
  const name = setupName.value.trim();
  const phone = setupPhone.value.trim();
  if (!name || !phone) return;

  saveProfile({ name, phone });
  renderProfile();
  showScreen("home");
}

async function importRisTimetables() {
  const username = risUsername.value.trim();
  const password = risPassword.value;
  if (!username || !password) {
    risImportStatus.textContent = "Bitte Benutzername und Passwort eingeben.";
    return;
  }

  risImportStatus.textContent = "RIS Fahrplandaten werden abgerufen...";

  try {
    const response = await fetch("api/ris-import", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      cache: "no-store",
      body: JSON.stringify({ username, password })
    });

    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      risImportStatus.textContent = payload.error || "RIS Fahrplandaten konnten nicht geladen werden.";
      return;
    }

    if (!Array.isArray(payload.trains) || !payload.trains.length) {
      risImportStatus.textContent = "RIS Antwort enthielt keine Fahrpläne.";
      return;
    }

    trains = payload.trains;
    saveTrains();
    window.localStorage.setItem(DATA_VERSION_KEY, payload.version || new Date().toISOString());
    currentTrain = trains[0];
    currentPreviewTrain = trains[0];
    risPassword.value = "";
    risImportStatus.textContent = `Übertragen: ${trains.length} Fahrpläne.`;
    renderCurrentDataScreen();
  } catch {
    risImportStatus.textContent = "RIS Import ist aktuell nicht erreichbar.";
  }
}

async function checkForAppUpdate() {
  try {
    const response = await fetch(`version.json?v=${Date.now()}`, { cache: "no-store" });
    if (!response.ok) return;

    const info = await response.json();
    const version = String(info.version || "").trim();
    if (!version) return;

    const storedVersion = window.localStorage.getItem("ris-app-version");
    if (!storedVersion) {
      window.localStorage.setItem("ris-app-version", version);
      return;
    }

    if (storedVersion !== version) {
      pendingAppVersion = version;
      updateDialog.classList.remove("hidden");
    }
  } catch {
    // Offline mode cannot check for a new Cloudflare deployment.
  }
}

async function checkForDataUpdate(initial = false) {
  if (dataUpdateInProgress) return;
  dataUpdateInProgress = true;

  try {
    let response = await fetch(`api/data-version?v=${Date.now()}`, { cache: "no-store" });
    if (!response.ok) {
      response = await fetch(`data/data-version.json?v=${Date.now()}`, { cache: "no-store" });
    }
    if (!response.ok) return;

    const info = await response.json();
    const version = String(info.version || "").trim();
    if (!version) return;

    const storedVersion = window.localStorage.getItem(DATA_VERSION_KEY);
    if (!storedVersion) {
      window.localStorage.setItem(DATA_VERSION_KEY, version);
      return;
    }

    if (storedVersion !== version) {
      await loadAppData();
      window.localStorage.setItem(DATA_VERSION_KEY, version);
      if (!initial) renderCurrentDataScreen();
    }
  } catch {
    // Datenaktualisierung bleibt still, wenn Cloudflare oder Netzwerk gerade nicht erreichbar ist.
  } finally {
    dataUpdateInProgress = false;
  }
}

function renderCurrentDataScreen() {
  if (!screens.search.classList.contains("hidden")) renderSearchResult();
  if (!screens.stationSearch.classList.contains("hidden")) renderStationSearchResult();
  if (!screens.stationBoard.classList.contains("hidden")) renderStationBoard();
  if (!screens.preview.classList.contains("hidden")) {
    const updatedPreview = trains.find((train) => train.number === currentPreviewTrain?.number);
    if (updatedPreview) {
      currentPreviewTrain = updatedPreview;
      openTrainPreview(updatedPreview);
    }
  }
  if (!screens.journey.classList.contains("hidden")) {
    const updatedTrain = trains.find((train) => train.number === currentTrain?.number);
    if (updatedTrain) {
      currentTrain = updatedTrain;
      renderRoute();
    }
  }
}

function dismissUpdate() {
  updateDialog.classList.add("hidden");
}

async function installUpdate() {
  if (pendingAppVersion) {
    window.localStorage.setItem("ris-app-version", pendingAppVersion);
  }

  try {
    if (navigator.serviceWorker) {
      const registrations = await navigator.serviceWorker.getRegistrations();
      await Promise.all(registrations.map((registration) => registration.unregister()));
    }

    if (window.caches) {
      const keys = await caches.keys();
      await Promise.all(keys.map((key) => caches.delete(key)));
    }
  } catch {
    // Reload still gives the browser a chance to fetch the new deployment.
  }

  window.location.replace(`${window.location.pathname}?update=${Date.now()}`);
}

function bookRole(role) {
  closeRoleSheet();
  addRecentBooking(currentTrain, selectedDate);
  saveActiveBooking(currentTrain, role, selectedDate);
  journeyTitle.textContent = `${currentTrain.line} (${currentTrain.number})`;
  renderRoute(role);
  showScreen("journey");
}

function saveActiveBooking(train, role, dateKey) {
  const booking = {
    number: train.number,
    line: train.line,
    role,
    dateKey,
    bookedAt: new Date().toISOString()
  };

  window.localStorage.setItem(ACTIVE_BOOKING_KEY, JSON.stringify(booking));
  publishActiveBooking(booking);
}

async function publishActiveBooking(booking) {
  try {
    await fetch("api/active-booking", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(booking)
    });
  } catch {
    // Lokaler Modus: Der Monitor auf demselben Browser nutzt weiterhin localStorage.
  }
}

async function clearPublishedActiveBooking() {
  try {
    await fetch("api/active-booking", { method: "DELETE" });
  } catch {
    // Offline oder lokaler Modus: lokale Ausbuchung reicht dann.
  }
}

function addRecentBooking(train, dateKey) {
  const dayLabel = dayLabelFor(dateKey);
  recentBookings = recentBookings.filter((booking) => !(booking.number === train.number && booking.dateKey === dateKey));
  recentBookings.unshift({
    number: train.number,
    line: train.line,
    dateKey,
    dayLabel
  });
  saveRecentBookings();
}

function dayLabelFor(dateKey) {
  if (dateKey === "tomorrow") return "Morgen";
  if (dateKey === "yesterday") return "Gestern";
  return "Heute";
}

function renderRoute(role) {
  routeScroll.innerHTML = `
    <ol class="route-list">
      ${currentTrain.stops.map((stop, index) => {
        const isFirst = index === 0;
        const isLast = index === currentTrain.stops.length - 1;
        return `
          <li class="${isFirst ? "first" : ""} ${isLast ? "last" : ""}">
            <span class="route-node"></span>
            <div class="stop-main">
              <b>${escapeHtml(stop[0])}</b>
              ${stop[1] ? `<span>an ${escapeHtml(stop[1])}</span>` : ""}
              ${stop[2] ? `<span>ab ${escapeHtml(stop[2])}</span>` : ""}
            </div>
            <span class="platform">Gl. ${escapeHtml(stop[3])}</span>
          </li>
        `;
      }).join("")}
    </ol>
  `;
  routeScroll.scrollTop = 0;
}

function openTrainPreview(train = currentTrain) {
  currentPreviewTrain = train;
  previewReturnScreen = lastScreen === "stationBoard" ? "stationBoard" : "search";
  previewTitle.textContent = `${train.line} (${train.number})`;
  previewRoute.innerHTML = renderRouteList(train);
  showScreen("preview");
  previewRoute.scrollTop = 0;
}

function renderRouteList(train) {
  return `
    <ol class="route-list">
      ${train.stops.map((stop, index) => {
        const isFirst = index === 0;
        const isLast = index === train.stops.length - 1;
        return `
          <li class="${isFirst ? "first" : ""} ${isLast ? "last" : ""}">
            <span class="route-node"></span>
            <div class="stop-main">
              <b>${escapeHtml(stop[0])}</b>
              ${stop[1] ? `<span>an ${escapeHtml(stop[1])}</span>` : ""}
              ${stop[2] ? `<span>ab ${escapeHtml(stop[2])}</span>` : ""}
            </div>
            <span class="platform">Gl. ${escapeHtml(stop[3])}</span>
          </li>
        `;
      }).join("")}
    </ol>
  `;
}

function openStationBoard(station) {
  selectedStation = station;
  stationBoardTitle.textContent = `${station.name} (${station.ril100})`;
  stationBoardStartMinutes = bestBoardStartForStation(station);
  stationBoardDateInput.value = formatIsoDate(stationBoardDate);
  stationBoardTimeInput.value = formatClock(stationBoardStartMinutes);
  renderStationBoard();
  showScreen("stationBoard");
}

function bestBoardStartForStation(station) {
  const times = trains
    .map((train) => {
      const index = train.stops.findIndex((stop) => stationMatchesStop(station, stop));
      if (index < 0) return null;
      const stop = train.stops[index];
      const time = stationBoardKind === "arrival" ? (stop[1] || stop[2]) : (stop[2] || stop[1]);
      const minutes = timeToMinutes(time);
      return minutes >= 99999 ? null : minutes;
    })
    .filter((minutes) => minutes !== null)
    .sort((a, b) => a - b);

  if (!times.length) return stationBoardStartMinutes;
  return Math.max(0, times[0] - 30);
}

function renderStationBoard() {
  const station = selectedStation;
  if (!station) return;

  stationBoardMode.textContent = stationBoardKind === "arrival" ? "Ankunft" : "Abfahrt";
  stationBoardDateButton.textContent = formatBoardDate(stationBoardDate);
  stationBoardTime.textContent = `${stationBoardKind === "arrival" ? "An" : "Ab"} ${formatClock(stationBoardStartMinutes)} (90min)`;

  const rows = trains
    .map((train) => {
      const index = train.stops.findIndex((stop) => stationMatchesStop(station, stop));
      if (index < 0) return null;
      const stop = train.stops[index];
      const time = stationBoardKind === "arrival" ? (stop[1] || stop[2]) : (stop[2] || stop[1]);
      if (!time) return null;
      const minutes = timeToMinutes(time);
      const distance = minutes - stationBoardStartMinutes;
      if (distance < -30 || distance > 90) return null;
      const destination = stationBoardKind === "arrival" ? train.from : train.to;
      return { train, stop, time, destination, minutes };
    })
    .filter(Boolean)
    .sort((a, b) => a.minutes - b.minutes)
    .slice(0, 16);

  if (!rows.length) {
    stationBoardList.innerHTML = `<div class="station-empty">Keine ${stationBoardKind === "arrival" ? "Ank&uuml;nfte" : "Abfahrten"} f&uuml;r ${escapeHtml(station.name)} vorhanden</div>`;
    return;
  }

  stationBoardList.innerHTML = rows.map((row, index) => `
    <button class="station-board-row" type="button" data-board-train="${escapeHtml(row.train.number)}">
      <span class="board-train-icon"><img src="assets/icons/badge-train.png" alt=""></span>
      <span class="board-row-main">
        <b>${escapeHtml(row.train.line)} (${escapeHtml(row.train.number)})</b>
        <span>${escapeHtml(station.name)} &rarr; <b>${escapeHtml(row.destination)}</b></span>
        <em>${stationBoardKind === "arrival" ? "an" : "ab"} ${escapeHtml(row.time)} ${index === 1 ? `<span class="delay-chip">+0</span> <span style="color:#2f812e">${escapeHtml(row.time)}</span>` : ""}</em>
      </span>
      <span class="board-platform">Gl. ${escapeHtml(row.stop[3] || "")}</span>
    </button>
  `).join("");
}

function pickBoardDate() {
  stationBoardDateInput.value = formatIsoDate(stationBoardDate);
  openNativePicker(stationBoardDateInput);
}

function pickBoardTime() {
  stationBoardTimeInput.value = formatClock(stationBoardStartMinutes);
  openNativePicker(stationBoardTimeInput);
}

function openNativePicker(input) {
  if (typeof input.showPicker === "function") {
    input.showPicker();
    return;
  }

  input.focus();
  input.click();
}

function updateBoardDate(value) {
  if (!value) return;
  const parsed = new Date(`${value}T00:00:00`);
  if (Number.isNaN(parsed.getTime())) return;
  stationBoardDate = parsed;
  renderStationBoard();
}

function updateBoardTime(value) {
  const minutes = timeToMinutes(value);
  if (!Number.isFinite(minutes) || minutes >= 99999) return;
  stationBoardStartMinutes = minutes;
  renderStationBoard();
}

function shiftBoardWindow(minutes) {
  stationBoardStartMinutes += minutes;
  stationBoardTimeInput.value = formatClock(stationBoardStartMinutes);
  renderStationBoard();
}

function timeToMinutes(time) {
  const [hours, minutes] = String(time).split(":").map(Number);
  if (!Number.isFinite(hours) || !Number.isFinite(minutes)) return 99999;
  return hours * 60 + minutes;
}

function openSafetyCheck() {
  incidentAnswer = "";
  document.querySelectorAll("input[name='incident']").forEach((input) => {
    input.checked = false;
  });
  checkoutButton.disabled = true;
  checkoutButton.textContent = `Von ${currentTrain.line} (${currentTrain.number}) ausbuchen`;
  showScreen("safety");
}

function handleIncidentChoice(value) {
  incidentAnswer = value;
  checkoutButton.disabled = value !== "no";
}

function checkoutTrain() {
  if (incidentAnswer !== "no") return;
  window.localStorage.removeItem(ACTIVE_BOOKING_KEY);
  clearPublishedActiveBooking();
  showScreen("home");
}

function openEditor() {
  const train = trains.find((item) => item.number === trainSearch.value.trim()) || currentTrain || trains[0];
  editNumber.value = train.number;
  editLine.value = train.line;
  editFrom.value = train.from;
  editTo.value = train.to;
  editStops.value = train.stops.map((stop) => stop.join(";")).join("\n");
  refreshEditorDownloadLink();
  showScreen("editor");
}

function buildTrainFromEditor() {
  const number = editNumber.value.trim();
  const line = editLine.value.trim() || "RE";
  const stops = editStops.value.split("\n")
    .map((row) => row.trim())
    .filter(Boolean)
    .map((row) => {
      const cells = row.split(";").map((cell) => cell.trim());
      return [cells[0] || "Halt", cells[1] || "", cells[2] || "", cells[3] || ""];
    });

  if (!number || stops.length < 2) return null;

  return {
    number,
    line,
    from: editFrom.value.trim() || stops[0][0],
    to: editTo.value.trim() || stops[stops.length - 1][0],
    duration: estimateDuration(stops),
    stops
  };
}

function refreshEditorDownloadLink() {
  if (!editorDownloadLink) return;

  const train = buildTrainFromEditor();
  const exportTrains = train
    ? [...trains.filter((item) => item.number !== train.number), train]
    : trains;

  if (editorDownloadUrl) {
    URL.revokeObjectURL(editorDownloadUrl);
  }

  editorDownloadUrl = URL.createObjectURL(new Blob([
    `${JSON.stringify(exportTrains, null, 2)}\n`
  ], { type: "application/json" }));
  editorDownloadLink.href = editorDownloadUrl;
}

function saveEditor() {
  const train = buildTrainFromEditor();
  if (!train) return;

  trains = trains.filter((item) => item.number !== train.number);
  trains.push(train);
  currentTrain = train;
  saveTrains();
  refreshEditorDownloadLink();
  trainSearch.value = train.number;
  showScreen("search");
  renderSearchResult();
}

function estimateDuration(stops) {
  const start = stops[0][2] || stops[0][1];
  const end = stops[stops.length - 1][1] || stops[stops.length - 1][2];
  if (!start || !end) return "";
  const [sh, sm] = start.split(":").map(Number);
  const [eh, em] = end.split(":").map(Number);
  let minutes = eh * 60 + em - (sh * 60 + sm);
  if (minutes < 0) minutes += 24 * 60;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return `${hours} h ${rest} min`;
}

function trainSvg() {
  return `<img class="badge-train-icon" src="assets/icons/badge-train.png" alt=""><span class="badge-separator" aria-hidden="true"></span>`;
}

function emptyTrainIcon() {
  return `<img class="badge-train-icon" src="assets/icons/train-home.png" alt="">`;
}

function normalizeText(value) {
  return String(value || "")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/\s+/g, " ")
    .trim()
    .toUpperCase();
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (char) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#039;"
  }[char]));
}

window.addEventListener("load", async () => {
  await loadAppData();
  await checkForDataUpdate(true);
  checkForAppUpdate();
  window.setTimeout(() => {
    screens.splash.classList.add("hidden");
    if (profile) {
      screens.home.classList.remove("hidden");
    } else {
      screens.profileSetup.classList.remove("hidden");
    }
  }, 5000);
});

window.addEventListener("focus", () => checkForDataUpdate());

document.addEventListener("visibilitychange", () => {
  if (!document.hidden) checkForDataUpdate();
});

window.setInterval(() => checkForDataUpdate(), 60000);

document.addEventListener("click", (event) => {
  const actionTarget = event.target.closest("[data-action]");
  if (actionTarget) {
    const action = actionTarget.dataset.action;
    if (action === "open-search") openSearch("booking");
    if (action === "open-search-menu") openSearchMenu();
    if (action === "close-search-menu") showScreen("home");
    if (action === "back-search-menu") showScreen("searchMenu");
    if (action === "open-train-preview-search") openSearch("preview");
    if (action === "open-station-search") openStationSearch("info");
    if (action === "open-station-board-search") openStationSearch("board");
    if (action === "back-home") showScreen(searchMode === "preview" ? "searchMenu" : "home");
    if (action === "back-search") showScreen("search");
    if (action === "back-preview") showScreen(previewReturnScreen || "searchMenu");
    if (action === "close-preview") showScreen(previewReturnScreen || "searchMenu");
    if (action === "close-station-board") showScreen("searchMenu");
    if (action === "back-station-search") showScreen("stationSearch");
    if (action === "open-safety") openSafetyCheck();
    if (action === "close-safety") showScreen("journey");
    if (action === "checkout") checkoutTrain();
    if (action === "clear-search") {
      trainSearch.value = "";
      renderSearchResult();
      trainSearch.focus();
    }
    if (action === "clear-station-search") {
      stationSearch.value = "";
      renderStationSearchResult();
      stationSearch.focus();
    }
    if (action === "open-train-preview") openTrainPreview(currentTrain);
    if (action === "toggle-board-mode") {
      stationBoardKind = stationBoardKind === "departure" ? "arrival" : "departure";
      renderStationBoard();
    }
    if (action === "pick-board-date") pickBoardDate();
    if (action === "pick-board-time") pickBoardTime();
    if (action === "board-earlier") shiftBoardWindow(-30);
    if (action === "board-later") shiftBoardWindow(30);
    if (action === "open-roles") openRoleSheet();
    if (action === "close-sheet") closeRoleSheet();
    if (action === "open-app-menu") openAppMenu();
    if (action === "close-app-menu") closeAppMenu();
    if (action === "open-impressum") openImpressum();
    if (action === "close-impressum") showScreen("home");
    if (action === "open-settings") openSettings();
    if (action === "back-settings-home") showScreen("home");
    if (action === "back-to-settings") showScreen("settings");
    if (action === "reset-login") requestResetLogin();
    if (action === "cancel-reset-login") cancelResetLogin();
    if (action === "confirm-reset-login") resetLogin();
    if (action === "open-notifications") openNotifications();
    if (action === "open-profile") openProfile();
    if (action === "open-appearance") openAppearance();
    if (action === "open-ris-import") openRisImport();
    if (action === "dismiss-update") dismissUpdate();
    if (action === "install-update") installUpdate();
    if (action === "open-editor") openEditor();
    if (action === "close-editor") showScreen(lastScreen || "home");
    if (action === "save-editor") saveEditor();
  }

  const dateButton = event.target.closest("[data-date]");
  if (dateButton) {
    selectedDate = dateButton.dataset.date;
    renderSearchResult();
  }

  const recentButton = event.target.closest("[data-recent-number]");
  if (recentButton) {
    const train = trains.find((item) => item.number === recentButton.dataset.recentNumber);
    if (train) {
      currentTrain = train;
      selectedDate = recentButton.dataset.recentDate || "today";
      trainSearch.value = train.number;
      renderSearchResult();
    }
  }

  const stationButton = event.target.closest("[data-station-ril100]");
  if (stationButton) {
    const station = stations.find((item) => item.ril100 === stationButton.dataset.stationRil100);
    if (station) {
      stationSearch.value = station.name;
      openStationBoard(station);
    }
  }

  const boardTrainButton = event.target.closest("[data-board-train]");
  if (boardTrainButton) {
    const train = trains.find((item) => item.number === boardTrainButton.dataset.boardTrain);
    if (train) openTrainPreview(train);
  }

  const roleButton = event.target.closest("[data-role]");
  if (roleButton) bookRole(roleButton.dataset.role);

  const stepButton = event.target.closest("[data-step-target]");
  if (stepButton) {
    const target = stepButton.dataset.stepTarget;
    const step = Number(stepButton.dataset.step || 0);
    preferences[target] = Math.max(0, Number(preferences[target] || 0) + step);
    savePreferences();
    renderPreferences();
  }

  const toggleButton = event.target.closest("[data-toggle]");
  if (toggleButton) {
    const key = toggleButton.dataset.toggle;
    preferences[key] = !preferences[key];
    savePreferences();
    renderPreferences();
  }

  const themeButton = event.target.closest("[data-theme]");
  if (themeButton) {
    preferences.theme = themeButton.dataset.theme;
    savePreferences();
    renderAppearance();
  }
});

profileSetupForm.addEventListener("submit", (event) => {
  event.preventDefault();
  saveProfileSetup();
});

risImportForm.addEventListener("submit", (event) => {
  event.preventDefault();
  importRisTimetables();
});

editorForm.addEventListener("input", refreshEditorDownloadLink);

document.addEventListener("change", (event) => {
  const incidentInput = event.target.closest("input[name='incident']");
  if (incidentInput) handleIncidentChoice(incidentInput.value);
});

searchForm.addEventListener("submit", (event) => {
  event.preventDefault();
  renderSearchResult();
  trainSearch.blur();
});

trainSearch.addEventListener("input", renderSearchResult);

stationSearchForm.addEventListener("submit", (event) => {
  event.preventDefault();
  const station = findStationByCodeOrName(stationSearch.value);
  if (station) {
    stationSearch.blur();
    openStationBoard(station);
    return;
  }
  renderStationSearchResult();
});

stationSearch.addEventListener("input", () => {
  window.clearTimeout(stationSearchTimer);
  stationSearchTimer = window.setTimeout(renderStationSearchResult, 90);
});

stationBoardDateInput.addEventListener("change", () => updateBoardDate(stationBoardDateInput.value));
stationBoardTimeInput.addEventListener("change", () => updateBoardTime(stationBoardTimeInput.value));
window.addEventListener("beforeunload", () => {
  if (editorDownloadUrl) URL.revokeObjectURL(editorDownloadUrl);
});

function installImageFallbacks() {
  document.addEventListener("error", (event) => {
    const image = event.target;
    if (!(image instanceof HTMLImageElement) || image.dataset.fallbackIcon === "true") {
      return;
    }

    image.dataset.fallbackIcon = "true";
    image.src = fallbackIconFor(image.getAttribute("src") || "");
  }, true);
}

function fallbackIconFor(src) {
  const name = src.split("/").pop() || "";
  const stroke = "#22252d";
  const muted = "#9aa1ad";

  if (name.includes("db-logo")) {
    return svgData(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 90 58">
        <rect x="5" y="5" width="80" height="48" rx="5" fill="#fff" stroke="#e2001a" stroke-width="6"/>
        <text x="45" y="41" text-anchor="middle" font-family="Arial, sans-serif" font-size="34" font-weight="900" fill="#e2001a">DB</text>
      </svg>`);
  }

  if (name.includes("search")) {
    return svgData(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" fill="none" stroke="${stroke}" stroke-width="5" stroke-linecap="round">
        <circle cx="27" cy="27" r="17"/>
        <path d="M40 40l15 15"/>
      </svg>`);
  }

  if (name.includes("menu")) {
    return svgData(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" fill="none" stroke="${stroke}" stroke-width="5" stroke-linecap="round">
        <path d="M14 20h36M14 32h36M14 44h36"/>
      </svg>`);
  }

  if (name.includes("back") || name.includes("logout")) {
    return svgData(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" fill="none" stroke="${stroke}" stroke-width="5" stroke-linecap="round" stroke-linejoin="round">
        <path d="M30 16L14 32l16 16"/>
        <path d="M16 32h34"/>
      </svg>`);
  }

  if (name.includes("bell")) {
    return svgData(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" fill="none" stroke="${stroke}" stroke-width="4" stroke-linecap="round" stroke-linejoin="round">
        <path d="M20 45h24"/>
        <path d="M24 45V28a8 8 0 0 1 16 0v17"/>
        <path d="M28 51a5 5 0 0 0 8 0"/>
      </svg>`);
  }

  if (name.includes("meinzug")) {
    return svgData(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" fill="none" stroke="${muted}" stroke-width="5" stroke-linecap="round" stroke-linejoin="round">
        <path d="M48 20H24L12 32l12 12h24"/>
        <path d="M24 32h26"/>
      </svg>`);
  }

  if (name.includes("zuglauf")) {
    return svgData(`
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" fill="none" stroke="${stroke}" stroke-width="4.5" stroke-linecap="round" stroke-linejoin="round">
        <path d="M15 18c12 0 10 16 22 16h12"/>
        <path d="M49 34l-7-7M49 34l-7 7"/>
        <path d="M15 46c12 0 10-16 22-16"/>
      </svg>`);
  }

  return svgData(`
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" fill="none" stroke="${stroke}" stroke-width="4" stroke-linecap="round" stroke-linejoin="round">
      <rect x="17" y="8" width="30" height="48" rx="8"/>
      <path d="M24 42h16"/>
      <circle cx="32" cy="49" r="2"/>
    </svg>`);
}

function svgData(svg) {
  return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg.trim())}`;
}
