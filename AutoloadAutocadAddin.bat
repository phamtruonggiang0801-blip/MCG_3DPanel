@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ===================================================
echo 🚀 CAI DAT PLUGIN KEM THEO COPY DU LIEU
echo ===================================================
echo.

:: ==========================================
:: 1. KHAI BÁO BIẾN
:: ==========================================
:: Đường dẫn thư mục đang chứa file .bat và file .dll (VD: C:\CustomTools)
set "SOURCE_DIR=%~dp0"

:: Thư mục đích trong ProgramData
set "BUNDLE_DIR=%PROGRAMDATA%\Autodesk\ApplicationPlugins\SDSPlugin.bundle"
set "CONTENTS_DIR=%BUNDLE_DIR%\Contents"
set "XML_FILE=%BUNDLE_DIR%\PackageContents.xml"

:: ==========================================
:: 2. TẠO THƯ MỤC VÀ KIỂM TRA QUYỀN
:: ==========================================
echo [1/3] Dang tao cau truc thu muc chuan cua Autodesk...
if not exist "%CONTENTS_DIR%" mkdir "%CONTENTS_DIR%"

if not exist "%CONTENTS_DIR%" (
    echo [LOI] Khong the tao thu muc. Vui long click chuot phai vao file .bat va chon "Run as Administrator".
    pause
    exit /b
)

:: ==========================================
:: 3. COPY TOÀN BỘ FILE DLL VÀO THƯ MỤC CONTENTS
:: ==========================================
echo [2/3] Dang copy cac file DLL vao thu muc Contents...
:: Copy tất cả file đuôi .dll nằm cùng chỗ với file .bat này
copy "%SOURCE_DIR%*.dll" "%CONTENTS_DIR%\" /Y >nul
:: Nếu có appsettings.txt thì copy luôn
if exist "%SOURCE_DIR%appsettings.txt" copy "%SOURCE_DIR%appsettings.txt" "%CONTENTS_DIR%\" /Y >nul

:: ==========================================
:: 4. TẠO FILE XML CHỈ ĐIỂM
:: ==========================================
echo [3/3] Dang cau hinh PackageContents.xml...

(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<ApplicationPackage SchemaVersion="1.0" AppVersion="1.0.0" Author="SDS Team" Name="SDSPlugin"^>
echo   ^<Components^>
echo     ^<RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="R24.0" /^>
echo     ^<ComponentEntry AppName="SDSPlugin" Version="1.0.0" ModuleName="./Contents/SDS.dll" AppDescription="SDS AutoCAD Plugin" LoadOnAutoCADStartup="True" /^>
echo   ^</Components^>
echo ^</ApplicationPackage^>
) > "%XML_FILE%"

echo.
echo ===================================================
echo ✅ HOAN THANH XUAT SAC!
echo Kiem tra ket qua tai: %BUNDLE_DIR%
echo ===================================================
pause
