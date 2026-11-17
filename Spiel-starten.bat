@echo off
rem ============================================================================
rem  Spiel-starten.bat  -  Fable Space Engine per Docker starten
rem
rem  Doppelklick genuegt. Docker Desktop muss laufen. Die Datei
rem    1. prueft, ob Docker installiert ist und laeuft,
rem    2. baut das Image (F# nach JS nach nginx) und startet den Container,
rem    3. waehlt automatisch einen freien Port (ab 8088),
rem    4. wartet, bis der Webserver antwortet, und oeffnet das Spiel im Browser.
rem
rem  Beenden:  "docker compose down" in diesem Ordner, oder den Container
rem            "fable-space-engine" in Docker Desktop stoppen.
rem  Eigener Startport:  vorher  set WEB_PORT=9000  setzen.
rem  Parameter /q:  ohne Browser und ohne Wartepausen (fuer Skripte und Tests).
rem ============================================================================
setlocal EnableExtensions
title Fable Space Engine - Docker-Start
cd /d "%~dp0"

set "QUIET="
if /i "%~1"=="/q" set "QUIET=1"

echo.
echo  Fable Space Engine - Docker-Start
echo  ---------------------------------
echo.

rem --- 1. Docker vorhanden und gestartet? -------------------------------------
where docker >nul 2>&1
if errorlevel 1 goto :nodocker

docker info >nul 2>&1
if errorlevel 1 goto :dockerstopped

rem --- 2. Alten Container desselben Projekts entfernen (gibt seinen Port frei) --
docker compose down >nul 2>&1

rem --- 3. Freien Port suchen --------------------------------------------------
if not defined WEB_PORT set "WEB_PORT=8088"
set "WANTED_PORT=%WEB_PORT%"
set /a MAX_PORT=WEB_PORT+30

:findport
powershell -NoProfile -Command "if (Get-NetTCPConnection -LocalPort %WEB_PORT% -State Listen -ErrorAction SilentlyContinue) { exit 1 }" >nul 2>&1
if not errorlevel 1 goto :portfound
set /a WEB_PORT+=1
if %WEB_PORT% GTR %MAX_PORT% goto :noport
goto :findport

:portfound
if not "%WEB_PORT%"=="%WANTED_PORT%" echo  Port %WANTED_PORT% ist belegt, es wird Port %WEB_PORT% verwendet.

rem --- 4. Image bauen und Container starten -----------------------------------
echo  Baue Image und starte Container.
echo  Beim ersten Mal dauert das einige Minuten, danach wenige Sekunden.
echo.
docker compose up -d --build
if errorlevel 1 goto :buildfailed

rem --- 5. Warten, bis der Webserver antwortet (maximal 30 Sekunden) -----------
echo.
echo  Warte auf den Webserver ...
set /a TRIES=0

:waitloop
powershell -NoProfile -Command "try { Invoke-WebRequest -UseBasicParsing -Uri http://localhost:%WEB_PORT%/ -TimeoutSec 2 | Out-Null; exit 0 } catch { exit 1 }" >nul 2>&1
if not errorlevel 1 goto :ready
set /a TRIES+=1
if %TRIES% GEQ 30 goto :notready
ping -n 2 127.0.0.1 >nul
goto :waitloop

:ready
echo.
echo  Fertig. Das Spiel laeuft unter:  http://localhost:%WEB_PORT%
echo.
echo  Beenden: "docker compose down" in diesem Ordner
echo           oder den Container "fable-space-engine" in Docker Desktop stoppen.
if defined QUIET goto :done
start "" "http://localhost:%WEB_PORT%"
echo.
echo  Dieses Fenster schliesst sich in 8 Sekunden.
ping -n 9 127.0.0.1 >nul
goto :done

:notready
echo.
echo  [WARNUNG] Der Container laeuft, antwortet aber noch nicht.
echo  Versuche es in einigen Sekunden unter http://localhost:%WEB_PORT%
echo  Logs ansehen:  docker compose logs
goto :fail

:nodocker
echo  [FEHLER] Docker wurde nicht gefunden.
echo  Bitte Docker Desktop installieren: https://www.docker.com/products/docker-desktop/
goto :fail

:dockerstopped
echo  [FEHLER] Docker laeuft nicht.
echo  Bitte Docker Desktop starten, warten bis es bereit ist, und diese Datei erneut ausfuehren.
goto :fail

:noport
echo  [FEHLER] Kein freier Port zwischen %WANTED_PORT% und %MAX_PORT% gefunden.
goto :fail

:buildfailed
echo.
echo  [FEHLER] Bauen oder Starten ist fehlgeschlagen. Die Meldungen stehen weiter oben.
goto :fail

:fail
echo.
if not defined QUIET pause
endlocal
exit /b 1

:done
endlocal
exit /b 0
