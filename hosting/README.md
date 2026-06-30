# RIS-Communicator mobil hosten

Damit die App auch erreichbar ist, wenn der PC aus ist, muss die Web-App auf einem externen Hosting liegen.
Die mobile App ist statisch und kann z.B. bei Cloudflare Pages, Netlify, GitHub Pages oder auf einem Webserver gehostet werden.

## Paket bauen

Im Projektordner ausführen:

```powershell
.\scripts\build-mobile-hosting.ps1
```

Danach liegt das fertige Upload-Paket hier:

```text
dist\mobile-app
```

## Was wird veröffentlicht?

- `index.html`
- `styles.css`
- `app.js`
- `manifest.webmanifest`
- `service-worker.js`
- `assets\...`
- `data\trains.json`

Die Datei `data\trains.json` enthält den letzten Stand, den der Windows-Editor exportiert hat.

## Wichtig

Der Windows-Editor und der lokale RIS-Proxy werden nicht öffentlich gehostet. Das ist absichtlich so, damit API-Zugangsdaten nicht ins Internet gelangen.
Wenn neue Zugdaten gepflegt wurden, einmal im Editor speichern und danach das Hosting-Paket neu bauen und hochladen.

## Cloudflare Pages Kurzweg

1. `.\scripts\build-mobile-hosting.ps1` ausführen.
2. Bei Cloudflare Pages ein neues Projekt anlegen.
3. Den Inhalt von `dist\mobile-app` hochladen.
4. Die angezeigte `https://...pages.dev` URL auf dem iPhone öffnen.
5. In Safari: Teilen -> Zum Home-Bildschirm.

HTTPS ist wichtig, damit iOS die PWA-Funktionen und das Homescreen-Icon sauber nutzt.
