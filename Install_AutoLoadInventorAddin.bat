@echo off
setlocal

:: 1. Định nghĩa các đường dẫn
set "SOURCE_DIR=C:\CustomTools\Inventor"
set "ADDIN_NAME=InventorShipDesign.addin"
set "INVENTOR_USER_ADDIN=%APPDATA%\Autodesk\Inventor 2023\Addins"

echo -----------------------------------------------------
echo  KICH HOAT ADD-IN TU CUSTOM TOOLS
echo -----------------------------------------------------

:: 2. Kiểm tra file nguồn tại C:\CustomTools\Inventor
if not exist "%SOURCE_DIR%\%ADDIN_NAME%" (
    echo [LOI] Khong tim thay file %ADDIN_NAME% tai %SOURCE_DIR%
    pause
    exit /b
)

:: 3. Tạo thư mục Addins của User nếu chưa có
if not exist "%INVENTOR_USER_ADDIN%" mkdir "%INVENTOR_USER_ADDIN%"

:: 4. Copy file .addin vào AppData (Khong can Admin)
echo [+] Dang kich hoat Add-in...
copy /Y "%SOURCE_DIR%\%ADDIN_NAME%" "%INVENTOR_USER_ADDIN%\"

if %errorlevel% equ 0 (
    echo [OK] Da kich hoat xong!
    echo File hien tai: %INVENTOR_USER_ADDIN%\%ADDIN_NAME%
) else (
    echo [LOI] Khong the copy file. Hãy kiem tra quyen truy cap.
)

echo -----------------------------------------------------
echo Hay khoi dong lai Inventor 2023.
pause
