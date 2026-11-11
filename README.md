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
| `GET` | `/api/hardware/devices/{id}/remote-overview` | Gesamtzustand: App, Tools, Reparatur, HWiNFO, Stresstests, Akkutest, Aufzeichnung |
| `GET` | `/api/hardware/devices/{id}/remote/{pfad}` | Weiterleitung an `GET http://<client>:8085/api/{pfad}` |
| `POST` | `/api/hardware/devices/{id}/remote/{pfad}` | Weiterleitung an `POST http://<client>:8085/api/{pfad}` |
| `POST` | `/api/hardware/devices/{id}/control/{aktion}` | Weiterleitung an `POST http://<client>:8085/api/control/{aktion}` (bestehend) |

**Abfragen (`GET .../remote/...`)**: `app/state`, `diagnostics`, `system`, `battery`, `telemetry`, `status`, `tools`, `repair`, `hwinfo`, `stress`, `stress/cpu`, `stress/ram`, `stress/gpu`, `battery-test`, `recording?samples=true&last=N`, `recording/samples`, `recording/export` (CSV), `report/system` (PDF), `report/battery` (PDF).

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
| `tools/refresh`, `tools/{OCCT\|FurMark\|Prime95\|Victoria}/launch` | – |
| `tools/{key}/path` / `tools/{key}/clear-path` | `{ path }` / – |
| `repair/{sfc\|dism_check\|dism_restore\|chkdsk}` | – |
| `hwinfo/start` | – |
| `ui/tab` | `{ tab: 0-6 \| dashboard \| tools \| repair \| info \| battery \| cpu \| ram }` |
| `ui/status` | `{ message, isError? }` |
| `ui/window` | `{ action: minimize \| maximize \| restore \| focus }` |
| `power/sleep` | `{ preventSleep: true \| false }` |
| `hardware-check/announce` | – |
| `app/exit` | – |

Beispiel:

```bash
curl -X POST http://localhost:5085/api/hardware/devices/<id>/remote/control/tools/OCCT/launch
curl -o bericht.pdf http://localhost:5085/api/hardware/devices/<id>/remote/report/system
```

