# CPM Hardware-Check (Next.js + C# Libre Hardware Monitor)

Modernes Hardware-Diagnose- und Monitoring-System im **CPM Corporate Design** (analog zum *Urlaubsantrag-* und *Angebotssystem-Projekt*), bestehend aus einem **Next.js (React) Frontend** und einem **C# ASP.NET Core (.NET 10) Backend** mit direkter **Libre Hardware Monitor (`LibreHardwareMonitorLib` & Port `8085` `/data.json`)** Integration.

---

## Funktionen

- **Port 8085 Listener & API-Abruf**:
  - Lauscht standardmäßig direkt auf **Port `8085`** (`http://localhost:8085`) sowie auf `5085`.
  - Stellt unter `http://localhost:8085/data.json` einen vollständig kompatiblen Libre Hardware Monitor JSON-Sensorbaum bereit und ruft gleichzeitig lokale sowie entfernte Libre Hardware Monitor Webserver (`http://<IP>:8085/data.json`) ab.
- **Netzwerk-Discovery (Port 8085 Scanner)**:
  - Scannt das lokale Subnetz parallel auf Port `8085` nach verfügbaren PCs/Prüfständen, auf denen Libre Hardware Monitor läuft, und bindet diese automatisch ein.
- **Hardware- & Komponenten-Übersicht**:
  - Zeigt alle verfügbaren Systeme sowie deren verbaute Hardware-Geräte (**CPU, GPU, Mainboard, SuperIO, RAM, NVMe/SSD/HDD, Netzwerkadapter, Lüfter**) mit Echtzeit-Temperaturen, Auslastungen, Taktraten, Spannungen und Leistungswerten (Watt).
- **Langzeit-Aufzeichnung & Absturz-Protokoll (Deutsche Zeitzone `Europe/Berlin`)**:
  - Zeichnet laufend Messpunkte aller überwachten Systeme auf.
  - Sobald die Verbindung zu einem Gerät auf Port `8085` abbricht, wird dies automatisch in **deutscher Zeitzone (`MEZ` / `MESZ`)** mit dem Wort **`Absturz`** sowie den letzten bekannten Sensorwerten (Temperatur/Last direkt vor dem Absturz) in `backend/data/absturz_protokoll.log` und im Web-Dashboard protokolliert.
  - Unterstützt den Export als `.log`-Datei (`GET /api/hardware/recording/export`) sowie das gezielte Testen eines Verbindungsabbruchs.
- **Fallback- & Demo-Modus**:
  - Unterstützt unter Windows direkt `LibreHardwareMonitorLib`, unter macOS/Linux reale Host-Telemetrie (`sysctl`, `DriveInfo`, `NetworkInterface`) sowie zuschaltbare Demo-Workstations zum sofortigen Testen.

---

## Schnellstart

```bash
./start.sh
```

Oder manuell:

1. **Frontend bauen (oder im Dev-Modus starten)**:
   ```bash
   cd frontend
   npm install
   npm run build   # Exportiert statisch nach ../backend/wwwroot
   # Alternativ für Live-Dev: npm run dev (http://localhost:3000)
   ```

2. **C# Backend starten (Port 8085)**:
   ```bash
   cd backend
   dotnet run
   ```

Danach ist das Dashboard direkt erreichbar unter:
- **Dashboard UI**: [http://localhost:8085](http://localhost:8085)
- **Libre Hardware Monitor JSON API**: [http://localhost:8085/data.json](http://localhost:8085/data.json)
- **Dashboard REST API**: [http://localhost:8085/api/hardware/dashboard](http://localhost:8085/api/hardware/dashboard)

---

## DiagnoseTool Fernsteuerung (REST-API)

Hardware-Check kann den **vollständigen Funktionsumfang** der DiagnoseTool-Desktop-App (ab Version 2.4.0) auf jedem verbundenen Rechner steuern. Das Backend leitet die Aufrufe an die API des DiagnoseTools (Port `8085` auf dem Client) weiter.

| Methode | Endpunkt | Beschreibung |
|---|---|---|
| `GET` | `/api/hardware/devices/{id}/remote` | Katalog aller Endpunkte des DiagnoseTools |
| `GET` | `/api/hardware/devices/{id}/remote-overview` | Gesamtzustand: App, FurMark, Reparatur, Stresstests, Akkutest, Aufzeichnung |
| `GET` | `/api/hardware/devices/{id}/remote/{pfad}` | Weiterleitung an `GET http://<client>:8085/api/{pfad}` |
| `POST` | `/api/hardware/devices/{id}/remote/{pfad}` | Weiterleitung an `POST http://<client>:8085/api/{pfad}` |
| `POST` | `/api/hardware/devices/{id}/control/{aktion}` | Weiterleitung an `POST http://<client>:8085/api/control/{aktion}` (bestehend) |

**Abfragen (`GET .../remote/...`)**: `app/state`, `diagnostics`, `system`, `battery`, `telemetry`, `status`, `tools`, `repair`, `stress`, `stress/cpu`, `stress/ram`, `stress/gpu`, `battery-test`, `recording?samples=true&last=N`, `recording/samples`, `recording/export` (CSV), `report/system` (PDF), `report/battery` (PDF).

**Aktionen (`POST .../remote/control/...` bzw. `POST .../control/...`)**:

| Aktion | Body |
|---|---|
| `stress/cpu/start` / `stress/cpu/stop` / `stress/cpu/config` | `{ durationMinutes?, threads? }` / – / `{ threads?, durationIndex? }` |
| `stress/ram/start` / `stress/ram/stop` / `stress/ram/config` | `{ durationMinutes?, targetPercent? }` / – / `{ targetPercent?, durationIndex? }` |
| `stress/gpu/donut` / `stress/gpu/gui` / `stress/gpu/stop` | `{ durationMinutes? }` |
| `stress/full/start` / `stress/full/stop` / `stress/stop-all` | `{ durationMinutes? }` |
| `battery-test/start` / `battery-test/stop` | `{ durationMinutes? (30), openVideo? (true) }` |
| `recording/start` / `recording/stop` / `recording/clear` | `{ orderNumber? }` |
| `order` | `{ orderNumber }` |
| `tools/refresh`, `tools/FurMark/launch` | – |
| `tools/{key}/path` / `tools/{key}/clear-path` | `{ path }` / – |
| `repair/{sfc\|dism_check\|dism_restore\|chkdsk}` | – |
| `ui/tab` | `{ tab: 0-6 \| dashboard \| tools \| repair \| info \| battery \| cpu \| ram }` |
| `ui/status` | `{ message, isError? }` |
| `ui/window` | `{ action: minimize \| maximize \| restore \| focus }` |
| `power/sleep` | `{ preventSleep: true \| false }` |
| `hardware-check/announce` | – |
| `app/exit` | – |

Beispiel:

```bash
curl -X POST http://localhost:5085/api/hardware/devices/<id>/remote/control/tools/FurMark/launch
curl -o bericht.pdf http://localhost:5085/api/hardware/devices/<id>/remote/report/system
```


---

## Programm geschlossen vs. Absturz & Absturzanalyse (Windows-Ereignisanzeige)

Ab DiagnoseTool 2.5.0 unterscheidet Hardware-Check zwischen einem **ordnungsgemäßen Beenden** und einem **Absturz**:

| Situation | Anzeige im Web |
|---|---|
| DiagnoseTool wird normal beendet (Fenster schließen, Exit, „Tests abschließen") | **Programm geschlossen** (grauer Schnitt in der Aufzeichnung, kein Absturz) |
| Verbindung weg ohne Abmeldung | **ABSTURZ** (wie bisher) |
| Gleiche Sitzung meldet sich wieder | Eintrag wird zu **Netzwerkunterbrechung** umgestuft |
| Neue Sitzung nach Ausfall | DiagnoseTool wertet die **Windows-Ereignisanzeige** aus und sendet einen **Absturzbericht** |

Der Absturzbericht enthält die Art des Ausfalls (Bluescreen inkl. BugCheck-Code, nicht korrigierbarer Hardwarefehler (WHEA), schlagartiger Stromverlust (Kernel-Power 41), Hard-Reset per Power-Taste, geplanter Neustart, Programmabsturz oder externes Beenden), eine **Kritikalität** (`INFO` · `NIEDRIG` · `MITTEL` · `HOCH` · `KRITISCH`, Score 0–100), Kategorie, wahrscheinliche Ursachen, Maßnahmen, Befunde und den **vollständigen Auszug der Ereignisanzeige** (System + Anwendung) im Analysezeitraum. Berücksichtigt wird auch, was zuletzt lief (Stresstests, Temperaturen, Leistungsaufnahme).

Wird der Ausfall als geplanter Neustart oder als externes Beenden erkannt, stuft Hardware-Check den ursprünglichen ABSTURZ-Eintrag automatisch um.

| Methode | Endpunkt | Beschreibung |
|---|---|---|
| `POST` | `/api/hardware/devices/goodbye` | Abmeldung des DiagnoseTools beim normalen Beenden |
| `POST` | `/api/hardware/devices/crash-report` | Absturzbericht vom DiagnoseTool (automatisch nach Neustart) |
| `GET` | `/api/hardware/crash-reports?deviceId=` | Kurzfassungen aller Absturzberichte |
| `GET` | `/api/hardware/devices/{id}/crash-reports` | Absturzberichte eines Geräts |
| `GET` | `/api/hardware/crash-reports/{reportId}` | Vollständiger Bericht inkl. Ereignisanzeige-Auszug |
| `GET` | `/api/hardware/crash-reports/{reportId}/export` | Bericht als Textdatei |
| `POST` | `/api/hardware/devices/{id}/crash-analysis/pull?hours=24` | Ereignisanzeige eines Clients jetzt auswerten (`lastCrash=true`: letzten Bericht abholen) |

Direkt am DiagnoseTool (Port 8085): `GET /api/session`, `/api/crash/last`, `/api/crash/last/export`, `/api/eventlog?hours=24&levels=1,2,3,4&logs=System,Application`, `/api/eventlog/analysis?hours=24`, `/api/eventlog/export?hours=24`, `POST /api/control/crash/resend`.

> Für den vollständigen Zugriff auf die Ereignisanzeige sollte das DiagnoseTool mit Administratorrechten laufen.

---

## Auto-Update des DiagnoseTools

Ab DiagnoseTool 2.6.0 prüft das Programm beim Start und danach alle 6 Stunden das neueste GitHub-Release (`releases/latest`). Ist der Release-Tag neuer als die installierte Version, erscheint im DiagnoseTool eine Hinweisleiste mit **Jetzt aktualisieren**, **Versionshinweise** und **Später**. Im Hardware-Check-Dashboard zeigt die Gerätekarte zusätzlich **„DiagnoseTool-Update verfügbar“** mit einem Button, der das Update aus der Ferne startet.

Ablauf unter Windows: Die passende Exe (x64/arm64) wird geladen, Größe und SHA-256-Prüfsumme des Releases werden geprüft, die laufende Exe wird ersetzt und das Programm neu gestartet. Fehlen Schreibrechte, wird der Austausch nach UAC-Bestätigung mit Administratorrechten ausgeführt. Hardware-Check zeigt dabei „Programm geschlossen (Update wird installiert)“ statt eines Absturzes. Unter macOS wird das DMG geladen und geöffnet.

Fernsteuerung: `GET .../remote/update`, `POST .../remote/control/update/check`, `POST .../remote/control/update/install`. Die Update-Prüfung lässt sich mit der Umgebungsvariable `DIAGNOSETOOL_NO_UPDATE_CHECK=1` abschalten.
