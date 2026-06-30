const CACHE_NAME = "ris-communicator-v24";

const APP_SHELL = [
  "./",
  "./index.html",
  "./styles.css",
  "./app.js",
  "./manifest.webmanifest",
  "./tft/index.html",
  "./tft/tft.css",
  "./tft/tft.js",
  "./tft/img/box_bluebg.png",
  "./tft/img/box_whitebg.png",
  "./tft/img/solidline.png",
  "./data/trains.json",
  "./data/stations.json",
  "./data/data-version.json",
  "./assets/fonts/DBRISpos.ttf",
  "./assets/fonts/DBRISneg.ttf",
  "./assets/apple-touch-icon.png",
  "./assets/icon-192.png",
  "./assets/icon-512.png",
  "./assets/icons/back.png",
  "./assets/icons/badge-crop-debug.png",
  "./assets/icons/badge-train.png",
  "./assets/icons/bell-home.png",
  "./assets/icons/db-logo.png",
  "./assets/icons/journey-menu.png",
  "./assets/icons/journey-search.png",
  "./assets/icons/logout.png",
  "./assets/icons/menu.png",
  "./assets/icons/search.png",
  "./assets/icons/tab-bell.png",
  "./assets/icons/tab-meinzug.png",
  "./assets/icons/tab-zuglauf.png",
  "./assets/icons/train-home.png",
  "./assets/templates/home.jpg",
  "./assets/templates/journey-bottom.jpg",
  "./assets/templates/journey-middle.jpg",
  "./assets/templates/journey-top.jpg",
  "./assets/templates/role-sheet.jpg",
  "./assets/templates/safety.jpg",
  "./assets/templates/search-recent.jpg",
  "./assets/templates/splash.jpg"
];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.addAll(APP_SHELL))
  );
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) => Promise.all(
      keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))
    ))
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  const url = new URL(request.url);

  if (request.mode === "navigate") {
    event.respondWith(
      fetch(request)
        .then((response) => {
          const copy = response.clone();
          caches.open(CACHE_NAME).then((cache) => cache.put("./index.html", copy));
          return response;
        })
        .catch(() => caches.match("./index.html"))
    );
    return;
  }

  if (
    url.pathname.endsWith("/data/trains.json")
    || url.pathname.endsWith("/data/stations.json")
    || url.pathname.endsWith("/data/data-version.json")
    || url.pathname.endsWith("/version.json")
  ) {
    event.respondWith(
      fetch(request)
        .then((response) => {
          const copy = response.clone();
          caches.open(CACHE_NAME).then((cache) => cache.put(request, copy));
          return response;
        })
        .catch(() => caches.match(request))
    );
    return;
  }

  event.respondWith(
    caches.match(request).then((cached) => cached || fetch(request))
  );
});
