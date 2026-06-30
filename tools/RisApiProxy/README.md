# RIS API Proxy

Lokaler Proxy fuer RIS::Stations und RIS::Journeys.

Warum:
- Zugangsdaten bleiben im Backend und landen nicht in der Web-App.
- Die App bekommt spaeter ein einheitliches Format, auch wenn RIS intern anders strukturiert ist.
- Der Windows-Editor bleibt als Offline- und Fallback-Datenpflege bestehen.

## Lokale Konfiguration

1. `appsettings.local.example.json` kopieren nach `appsettings.local.json`.
2. Base-URLs und Auth-Header aus deinem DB/RIS-Testzugang eintragen.
3. Keine echten Zugangsdaten in Git oder Chat kopieren.

Wichtig fuer RIS Journeys: Im DB API Marketplace heisst der abonnierbare Pfad fuer deinen Zugang `ris-journeys-transporteure/v2`.

```json
{
  "RisApi": {
    "StationsBaseUrl": "https://apis.deutschebahn.com/db-api-marketplace/apis/ris-stations/v1",
    "JourneysBaseUrl": "https://apis.deutschebahn.com/db-api-marketplace/apis/ris-journeys-transporteure/v2",
    "AuthMode": "ApiKeyHeader",
    "ApiKeyHeaderName": "DB-Api-Key",
    "ApiKey": "<nur lokal eintragen>"
  }
}
```

Falls dein Zugang Client-ID/Client-Secret oder Bearer Token statt API-Key nutzt, trage die passenden Header-Namen in `appsettings.local.json` ein. Der Proxy unterstuetzt alle drei Varianten.

## Starten

```powershell
dotnet run --project .\tools\RisApiProxy\RisApiProxy.csproj --urls http://127.0.0.1:5068
```

## Test-Endpunkte

```text
GET http://127.0.0.1:5068/health
GET http://127.0.0.1:5068/api/config/status
```

Rohe RIS-Durchleitung:

```text
GET/POST http://127.0.0.1:5068/api/ris/stations/<pfad-aus-api-doku>?...
GET/POST http://127.0.0.1:5068/api/ris/journeys/<pfad-aus-yaml>?...
```

App-freundliche RIS-Journeys-Endpunkte:

```text
GET http://127.0.0.1:5068/api/search-train/4865?date=2026-06-18
GET http://127.0.0.1:5068/api/journey-by-number/4865?date=2026-06-18
GET http://127.0.0.1:5068/api/journey/<journeyID-aus-search>
```

`/api/search-train/{nummer}` liefert gefundene RIS-Journeys normalisiert plus `raw`.
`/api/journey-by-number/{nummer}` sucht zuerst per `/find`, nimmt den ersten Treffer und normalisiert den Zuglauf in das Format der App:

```json
{
  "source": "RIS",
  "journeyID": "...",
  "number": "4865",
  "line": "RE2 (4865)",
  "from": "Hof Hbf",
  "to": "Muenchen Hbf",
  "duration": "14:34 - 18:16",
  "stops": [
    ["Hof Hbf", "", "14:34", "6a"],
    ["Marktredwitz", "15:01", "15:01", "5"]
  ],
  "raw": {}
}
```
