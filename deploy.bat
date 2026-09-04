@echo off
setlocal EnableDelayedExpansion
rem  deploy.bat          -> Debug: build DLL kem timestamp (.NET 8), copy duong dan NETLOAD vao clipboard (hot reload)
rem  deploy.bat release  -> Release: build (MSBuild target tu deploy folder day du vao C:\CustomTools\Autocad\MCG_3DPanel)
rem
rem  .NET 8 (AutoCAD 2026): output nam thang o bin\<Cfg>\ (KHONG con thu muc net48).
rem  NETLOAD chi can duong dan .dll; AutoCAD tu doc .deps.json + .runtimeconfig.json cung thu muc.
rem  !!! NETLOAD DLL nay PHAI vao AutoCAD 2026 (.NET 8). Nap vao AutoCAD 2023/2024 (.NET FW) => crash 0xC0000005.

set "CFG=%~1"
if "%CFG%"=="" set "CFG=Debug"

echo === Build MCG_3DPanel (%CFG%) ===
dotnet build -c %CFG% "%~dp0MCG_3DPanel.csproj"
if errorlevel 1 (
    echo.
    echo *** BUILD THAT BAI *** — xem loi CS o tren.
    exit /b 1
)

if /I "%CFG%"=="Release" goto :release

rem ---------- DEBUG: hot reload ----------
set "OUT=%~dp0bin\Debug"
set "DLL="
for /f "delims=" %%f in ('dir /b /o-d "%OUT%\MCG_3DPanel_*.dll" 2^>nul') do (
    set "DLL=%OUT%\%%f"
    goto :gotdll
)
:gotdll
if not defined DLL (
    echo Khong tim thay DLL timestamp trong %OUT%
    exit /b 1
)
echo !DLL!| clip
echo.
echo === XONG (DEBUG - hot reload) ===
echo   DLL moi: !DLL!
echo   (Da copy duong dan vao clipboard)
echo   Trong AutoCAD 2026: go NETLOAD, dan (Ctrl+V) duong dan, Enter. KHONG can restart.
echo   Sau do go: MCG_Build3DPanel
goto :end

rem ---------- RELEASE: MSBuild target DeployToCustomTools da copy folder day du ----------
:release
echo.
echo === XONG (RELEASE) ===
echo   MSBuild target 'DeployToCustomTools' da deploy vao C:\CustomTools\Autocad\MCG_3DPanel\
echo   Khoi dong lai AutoCAD 2026 de tu dong nap. Sau do go: MCG_Build3DPanel

:end
endlocal
