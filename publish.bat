@echo off
setlocal

echo ========================================
echo   WorkReport publish
echo ========================================
echo.

set "PUBLISH_DIR=publish"
set "PRESERVE_DIR=tmp\publish_preserve"
set "HAD_DB=0"

if exist "%PRESERVE_DIR%" (
    rmdir /s /q "%PRESERVE_DIR%"
)
mkdir "%PRESERVE_DIR%" >nul 2>nul

echo [1/4] Preserving runtime data from existing publish folder...
if exist "%PUBLISH_DIR%\workreport.db" (
    set "HAD_DB=1"
    copy "%PUBLISH_DIR%\workreport.db" "%PRESERVE_DIR%\workreport.db" >nul
)
if exist "%PUBLISH_DIR%\workreport.db-wal" copy "%PUBLISH_DIR%\workreport.db-wal" "%PRESERVE_DIR%\workreport.db-wal" >nul
if exist "%PUBLISH_DIR%\workreport.db-shm" copy "%PUBLISH_DIR%\workreport.db-shm" "%PRESERVE_DIR%\workreport.db-shm" >nul
if exist "%PUBLISH_DIR%\Start-WorkReport.vbs" copy "%PUBLISH_DIR%\Start-WorkReport.vbs" "%PRESERVE_DIR%\Start-WorkReport.vbs" >nul
if exist "%PUBLISH_DIR%\Start-WorkReport-Silent.vbs" copy "%PUBLISH_DIR%\Start-WorkReport-Silent.vbs" "%PRESERVE_DIR%\Start-WorkReport-Silent.vbs" >nul
if exist "%PUBLISH_DIR%\app.ico" copy "%PUBLISH_DIR%\app.ico" "%PRESERVE_DIR%\app.ico" >nul
if exist "%PUBLISH_DIR%\backups" xcopy "%PUBLISH_DIR%\backups" "%PRESERVE_DIR%\backups\" /E /I /Y >nul

echo [2/4] Cleaning old publish folder...
if exist "%PUBLISH_DIR%" (
    rmdir /s /q "%PUBLISH_DIR%"
)

echo [3/4] Building self-contained Windows x64 publish...
dotnet publish -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=false ^
    -p:DebugType=none ^
    -p:DebugSymbols=false ^
    -o ./%PUBLISH_DIR%

if %ERRORLEVEL% NEQ 0 (
    echo Publish failed.
    pause
    exit /b 1
)

echo [4/4] Restoring runtime data...
if "%HAD_DB%"=="1" (
    copy "%PRESERVE_DIR%\workreport.db" "%PUBLISH_DIR%\workreport.db" >nul
    if exist "%PRESERVE_DIR%\workreport.db-wal" copy "%PRESERVE_DIR%\workreport.db-wal" "%PUBLISH_DIR%\workreport.db-wal" >nul
    if exist "%PRESERVE_DIR%\workreport.db-shm" copy "%PRESERVE_DIR%\workreport.db-shm" "%PUBLISH_DIR%\workreport.db-shm" >nul
) else (
    if exist "workreport.db" (
        copy "workreport.db" "%PUBLISH_DIR%\workreport.db" >nul
    )
)
if exist "%PRESERVE_DIR%\Start-WorkReport.vbs" copy "%PRESERVE_DIR%\Start-WorkReport.vbs" "%PUBLISH_DIR%\Start-WorkReport.vbs" >nul
if exist "%PRESERVE_DIR%\Start-WorkReport-Silent.vbs" copy "%PRESERVE_DIR%\Start-WorkReport-Silent.vbs" "%PUBLISH_DIR%\Start-WorkReport-Silent.vbs" >nul
if exist "%PRESERVE_DIR%\app.ico" copy "%PRESERVE_DIR%\app.ico" "%PUBLISH_DIR%\app.ico" >nul
if exist "%PRESERVE_DIR%\backups" xcopy "%PRESERVE_DIR%\backups" "%PUBLISH_DIR%\backups\" /E /I /Y >nul

echo.
echo ========================================
echo   Publish complete: .\publish\
echo   Runtime database was preserved when present.
echo   URL: http://localhost:51789
echo ========================================
echo.
pause
