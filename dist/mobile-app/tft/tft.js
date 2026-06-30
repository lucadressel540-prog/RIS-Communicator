const tftScreen = document.getElementById("tftScreen");
const trainLine = document.getElementById("trainLine");
const trainNumber = document.getElementById("trainNumber");
const trainOrigin = document.getElementById("trainOrigin");
const routeSeparator = document.getElementById("routeSeparator");
const trainDestination = document.getElementById("trainDestination");
const centerLabel = document.getElementById("centerLabel");
const centerStation = document.getElementById("centerStation");
const centerSubline = document.getElementById("centerSubline");
const routeTimes = document.getElementById("routeTimes");
const routeRows = document.getElementById("routeRows");
const routeRail = document.getElementById("routeRail");
const routeCaption = document.getElementById("routeCaption");
const clock = document.getElementById("uhr");
const trainSelect = document.getElementById("trainSelect");
const prevStop = document.getElementById("prevStop");
const nextStop = document.getElementById("nextStop");
const fullscreenButton = document.getElementById("fullscreenButton");

let trains = [];
let currentTrain = null;
let activeBooking = null;
let manualStopIndex = null;
let demoMode = false;

init();

async function init() {
  tickClock();
  window.setInterval(tickClock, 1000);
  window.setInterval(render, 15000);
  window.setInterval(refreshActiveBooking, 5000);

  trains = await loadTrains();
  activeBooking = await loadActiveBooking();
  demoMode = new URLSearchParams(window.location.search).has("demo");
  if (!activeBooking && demoMode && trains[0]) {
    activeBooking = { number: trains[0].number };
    manualStopIndex = 0;
  }

  if (!activeBooking) {
    showNoActiveBooking();
    return;
  }

  showActiveBooking(activeBooking);
}

async function loadTrains() {
  try {
    let response = await fetch(`../api/trains?v=${Date.now()}`, { cache: "no-store" });
    if (!response.ok) {
      response = await fetch(`../data/trains.json?v=${Date.now()}`, { cache: "no-store" });
    }
    if (!response.ok) return [];
    const data = await response.json();
    const source = Array.isArray(data) ? data : data?.trains;
    if (!Array.isArray(source)) return [];
    return source.map(normalizeTrain).filter((train) => train.number && train.stops.length);
  } catch {
    return [];
  }
}

function normalizeTrain(train) {
  const stops = Array.isArray(train.stops)
    ? train.stops.map((stop) => [
      String(stop?.[0] || "").trim(),
      String(stop?.[1] || "").trim(),
      String(stop?.[2] || "").trim(),
      String(stop?.[3] || "").trim()
    ]).filter((stop) => stop[0])
    : [];

  return {
    number: String(train.number || "").trim(),
    line: String(train.line || "").trim() || "RE",
    from: String(train.from || stops[0]?.[0] || "").trim(),
    to: String(train.to || stops[stops.length - 1]?.[0] || "").trim(),
    duration: String(train.duration || "").trim(),
    stops
  };
}

async function loadActiveBooking() {
  const cloud = await loadCloudActiveBooking();
  if (cloud.available) return cloud.booking;

  try {
    const parsed = JSON.parse(window.localStorage.getItem("ris-active-booking") || "null");
    return parsed && parsed.number ? parsed : null;
  } catch {
    return null;
  }
}

async function loadCloudActiveBooking() {
  try {
    const response = await fetch(`../api/active-booking?v=${Date.now()}`, { cache: "no-store" });
    if (!response.ok) return { available: false, booking: null };
    const data = await response.json();
    return { available: true, booking: data?.booking?.number ? data.booking : null };
  } catch {
    return { available: false, booking: null };
  }
}

async function refreshActiveBooking() {
  if (demoMode) return;
  const latest = await loadActiveBooking();
  const currentNumber = activeBooking?.number || "";
  const latestNumber = latest?.number || "";
  if (currentNumber === latestNumber) return;

  activeBooking = latest;
  manualStopIndex = null;

  if (!activeBooking) {
    currentTrain = null;
    showNoActiveBooking();
    return;
  }

  if (!trains.length) trains = await loadTrains();
  showActiveBooking(activeBooking);
}

function showActiveBooking(booking) {
  if (!trains.length) {
    showEmpty();
    return;
  }

  currentTrain = trains.find((train) => train.number === String(booking.number));
  if (!currentTrain) {
    showNoActiveBooking();
    return;
  }

  renderTrainOptions();
  render();
}

function renderTrainOptions() {
  trainSelect.innerHTML = trains.map((train) => `
    <option value="${escapeHtml(train.number)}">${escapeHtml(train.line)} ${escapeHtml(train.number)} ${escapeHtml(train.from)} - ${escapeHtml(train.to)}</option>
  `).join("");
  if (currentTrain) trainSelect.value = currentTrain.number;
}

function render() {
  if (!currentTrain) return;

  const state = manualStopIndex === null
    ? getTrainState(currentTrain)
    : getManualState(currentTrain, manualStopIndex);

  tftScreen.classList.remove("is-waiting");
  trainLine.textContent = currentTrain.line;
  trainNumber.textContent = "";
  trainOrigin.textContent = currentTrain.from;
  routeSeparator.textContent = currentTrain.from && currentTrain.to ? " - " : "";
  trainDestination.textContent = currentTrain.to;

  centerLabel.textContent = state.mode === "predeparture" ? "" : "Nächste Station";
  centerStation.textContent = state.displayStop[0];
  centerStation.classList.toggle("is-long", state.displayStop[0].length > 16);
  centerStation.classList.toggle("is-very-long", state.displayStop[0].length > 24);
  centerSubline.textContent = state.mode === "predeparture"
    ? `Abfahrt ${departureTime(state.displayStop)}`
    : `${state.isFinal ? "Ankunft" : "Ankunft"} ${arrivalTime(state.displayStop)}`;

  const onwardStops = buildOnwardStops(currentTrain, state.displayIndex, state.mode);
  routeCaption.textContent = state.mode === "predeparture" ? "Weiterfahrt nach:" : "Weiterfahrt nach:";
  routeTimes.innerHTML = onwardStops.map((item) => `
    <div class="route-time ${item.kind}">${escapeHtml(item.time)}</div>
  `).join("");
  routeRail.innerHTML = `
    <div class="rail-rows">
      ${onwardStops.map((item) => `
        <div class="rail-row ${item.kind}">
          ${item.kind === "stop" || item.kind === "final" ? "<span class=\"rail-marker\"></span>" : ""}
        </div>
      `).join("")}
    </div>
  `;
  routeRows.innerHTML = onwardStops.map((item) => `
    <div class="route-stop ${item.kind}">
      ${escapeHtml(item.label)}
    </div>
  `).join("");
}

function getTrainState(train) {
  const now = getNowMinutes();
  const firstIndex = 0;
  const lastIndex = train.stops.length - 1;
  const firstDeparture = timeToMinutes(departureTime(train.stops[firstIndex]));

  if (now < firstDeparture) {
    return {
      mode: "predeparture",
      displayIndex: firstIndex,
      displayStop: train.stops[firstIndex],
      isFinal: false
    };
  }

  for (let index = 1; index < train.stops.length; index += 1) {
    const stop = train.stops[index];
    const arrival = timeToMinutes(arrivalTime(stop));
    const departure = timeToMinutes(departureTime(stop));

    if (now < Math.max(arrival, departure)) {
      return {
        mode: "running",
        displayIndex: index,
        displayStop: stop,
        isFinal: index === lastIndex
      };
    }
  }

  return {
    mode: "running",
    displayIndex: lastIndex,
    displayStop: train.stops[lastIndex],
    isFinal: true
  };
}

function getManualState(train, index) {
  const displayIndex = Math.max(0, Math.min(train.stops.length - 1, index));
  return {
    mode: displayIndex === 0 ? "predeparture" : "running",
    displayIndex,
    displayStop: train.stops[displayIndex],
    isFinal: displayIndex === train.stops.length - 1
  };
}

function buildOnwardStops(train, displayIndex, mode) {
  const startIndex = mode === "predeparture" ? 1 : displayIndex + 1;
  const remaining = train.stops.slice(startIndex);
  const rows = [];

  remaining.slice(0, 4).forEach((stop, offset) => {
    rows.push({
      kind: "stop",
      time: arrivalTime(stop),
      label: stop[0]
    });
  });

  const finalStop = train.stops[train.stops.length - 1];
  const alreadyShowsFinal = rows.some((row) => row.label === finalStop[0]);
  if (remaining.length > 5 && !alreadyShowsFinal) {
    rows.push({ kind: "ellipsis", time: "", label: "..." });
    rows.push({ kind: "final", time: arrivalTime(finalStop), label: finalStop[0] });
  } else if (remaining.length && !alreadyShowsFinal) {
    rows.push({ kind: "final", time: arrivalTime(finalStop), label: finalStop[0] });
  }

  if (!rows.length) {
    rows.push({ kind: "final", time: arrivalTime(finalStop), label: "Endstation erreicht" });
  }

  return rows;
}

function arrivalTime(stop) {
  return stop?.[1] || stop?.[2] || "";
}

function departureTime(stop) {
  return stop?.[2] || stop?.[1] || "";
}

function timeToMinutes(value) {
  const [hours, minutes] = String(value || "").split(":").map(Number);
  if (!Number.isFinite(hours) || !Number.isFinite(minutes)) return 99999;
  return hours * 60 + minutes;
}

function getNowMinutes() {
  const now = new Date();
  return now.getHours() * 60 + now.getMinutes();
}

function setTrain(number) {
  const train = trains.find((item) => item.number === number);
  if (!train) return;
  currentTrain = train;
  activeBooking = { number };
  manualStopIndex = null;
  render();
}

function moveStop(delta) {
  if (!currentTrain) return;
  const state = manualStopIndex === null ? getTrainState(currentTrain) : getManualState(currentTrain, manualStopIndex);
  manualStopIndex = Math.max(0, Math.min(currentTrain.stops.length - 1, state.displayIndex + delta));
  render();
}

function tickClock() {
  clock.textContent = new Intl.DateTimeFormat("de-DE", {
    hour: "2-digit",
    minute: "2-digit"
  }).format(new Date());
}

function showEmpty() {
  tftScreen.classList.add("is-waiting");
  trainLine.textContent = "RIS";
  trainNumber.textContent = "";
  trainOrigin.textContent = "";
  routeSeparator.textContent = "";
  trainDestination.textContent = "";
  centerLabel.textContent = "";
  centerStation.textContent = "Keine Daten";
  centerSubline.textContent = "Bitte im Editor Zugdaten speichern.";
  routeTimes.innerHTML = "";
  routeRail.innerHTML = "";
  routeRows.innerHTML = "";
}

function showNoActiveBooking() {
  tftScreen.classList.add("is-waiting");
  trainLine.textContent = "RIS";
  trainNumber.textContent = "";
  trainOrigin.textContent = "Monitor wartet";
  routeSeparator.textContent = " ";
  trainDestination.textContent = "Keine aktive Einbuchung";
  centerLabel.textContent = "";
  centerStation.textContent = "Bitte in der App einbuchen";
  centerSubline.textContent = "Der Zuglauf erscheint nach erfolgreicher Rollen-Einbuchung.";
  routeTimes.innerHTML = "";
  routeRail.innerHTML = "";
  routeRows.innerHTML = "";
  trainSelect.innerHTML = "";
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (char) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    "\"": "&quot;",
    "'": "&#039;"
  }[char]));
}

trainSelect.addEventListener("change", () => setTrain(trainSelect.value));
prevStop.addEventListener("click", () => moveStop(-1));
nextStop.addEventListener("click", () => moveStop(1));
fullscreenButton.addEventListener("click", () => {
  const root = document.documentElement;
  if (!document.fullscreenElement && root.requestFullscreen) {
    root.requestFullscreen();
  } else if (document.exitFullscreen) {
    document.exitFullscreen();
  }
});

document.addEventListener("keydown", (event) => {
  if (event.key === "ArrowLeft") moveStop(-1);
  if (event.key === "ArrowRight" || event.key === " ") moveStop(1);
});

window.addEventListener("storage", (event) => {
  if (event.key === "ris-active-booking") window.location.reload();
});
