@echo off
setlocal EnableDelayedExpansion
rem  deploy.bat          -> Debug: build DLL kem timestamp, copy duong dan NETLOAD vao clipboard (hot reload, khong restart)
rem  deploy.bat release  -> Release: build DLL on dinh + cai bundle autoload vao ApplicationPlugins

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
set "OUT=%~dp0bin\Debug\net48"
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
echo   Trong AutoCAD: go NETLOAD, dan (Ctrl+V) duong dan, Enter. KHONG can restart.
echo   Sau do go: MCG_Build3DPanel
goto :end

rem ---------- RELEASE: bundle autoload ----------
:release
set "DLL=%~dp0bin\Release\net48\MCG_3DPanel.dll"
set "BUNDLE=%APPDATA%\Autodesk\ApplicationPlugins\MCG_3DPanel.bundle"
echo.
echo === Deploy Release toi %BUNDLE% ===
if not exist "%BUNDLE%\Contents" mkdir "%BUNDLE%\Contents"
copy /Y "%~dp0PackageContents.xml" "%BUNDLE%\PackageContents.xml" >nul
copy /Y "%DLL%" "%BUNDLE%\Contents\MCG_3DPanel.dll" >nul
echo === XONG (RELEASE - bundle autoload) ===
echo   Khoi dong lai AutoCAD de tu dong nap. Sau do go: MCG_Build3DPanel

:end
endlocal
