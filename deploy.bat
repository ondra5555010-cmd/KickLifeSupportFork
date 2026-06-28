@echo off
echo ========================================
echo  KICK Life Support - Deployment Script
echo ========================================
echo.

REM Build the project in Release mode
echo Building project...
where dotnet >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    dotnet build -c Release
) else (
    echo dotnet SDK not found, using Visual Studio Roslyn compiler...
    if not exist "bin\Release\net472" mkdir "bin\Release\net472"
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe" /nologo /target:library /out:bin\Release\net472\KickLifeSupport.dll /langversion:latest /reference:Libs\Assembly-CSharp.dll /reference:Libs\UnityEngine.dll /reference:Libs\UnityEngine.CoreModule.dll /reference:Libs\UnityEngine.IMGUIModule.dll /reference:Libs\SystemHeat.dll *.cs
)
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b 1
)
echo Build successful!
echo.

REM Define paths
set SOURCE_DLL=bin\Release\net472\KickLifeSupport.dll
set SOURCE_PACKAGE=GameData\KickLifeSupport
set DEST_DIR=%~dp0..\GameData\KickLifeSupport

REM Create destination directories if they don't exist
if not exist "%DEST_DIR%" mkdir "%DEST_DIR%"

REM Copy files
echo Deploying files to KSP...
if exist "%DEST_DIR%\KickLifeSupport.cfg" del /Q "%DEST_DIR%\KickLifeSupport.cfg"
if exist "%DEST_DIR%\Patches\35_RadiatorControl.cfg" del /Q "%DEST_DIR%\Patches\35_RadiatorControl.cfg"
xcopy /E /I /Y "%SOURCE_PACKAGE%\*" "%DEST_DIR%\"
copy /Y "%SOURCE_DLL%" "%DEST_DIR%\KickLifeSupport.dll"

echo.
echo ========================================
echo  Deployment complete!
echo ========================================
echo.

explorer "%~dp0..\GameData"
