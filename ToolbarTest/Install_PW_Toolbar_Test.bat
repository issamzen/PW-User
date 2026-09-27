@echo off
REM ==================================================================
REM  PW-User - CATIA toolbar TEST kit installer
REM  Copies the four test macros and their icons to
REM    %LOCALAPPDATA%\Estichara\PWUserTest\Macros
REM    %LOCALAPPDATA%\Estichara\PWUserTest\Icons
REM  Nothing is written to the CATIA settings: the macro library and
REM  the toolbar are declared once by hand (see README_TOOLBAR_TEST.md).
REM ==================================================================
setlocal
set ROOT=%LOCALAPPDATA%\Estichara\PWUserTest
set MACROS=%ROOT%\Macros
set ICONS=%ROOT%\Icons

echo.
echo  PW-User toolbar test kit
echo  ------------------------
echo  Target folder : %ROOT%
echo.

if not exist "%MACROS%" mkdir "%MACROS%"
if not exist "%ICONS%" mkdir "%ICONS%"

copy /Y "%~dp0PW_01_License.CATScript" "%MACROS%" >nul
copy /Y "%~dp0PW_02_Stroke.CATScript"  "%MACROS%" >nul
copy /Y "%~dp0PW_03_Library.CATScript" "%MACROS%" >nul
copy /Y "%~dp0PW_04_Checker.CATScript" "%MACROS%" >nul
copy /Y "%~dp0Icons\*.bmp"             "%ICONS%"  >nul

if errorlevel 1 (
  echo  [FAILED] the files could not be copied.
  pause
  exit /b 1
)

echo  [OK] 4 macros copied to : %MACROS%
echo  [OK] 4 icons  copied to : %ICONS%
echo.
echo  Next steps in CATIA (one time, about 3 minutes):
echo.
echo   1) Tools ^> Macro ^> Macros... ^> Macro libraries...
echo      Library type = Directory ^> Add existing library ^> pick:
echo      %MACROS%
echo      Close.
echo   2) Tools ^> Customize... ^> Commands tab ^> Categories: Macros
echo      Drag PW_01_License ... PW_04_Checker onto a toolbar.
echo   3) Select each one ^> Show properties... ^> Icon: ...
echo      pick the matching .bmp in: %ICONS%
echo   4) Close Customize. The 4 icons stay after restarting CATIA.
echo.
echo  Test order: License (1) first, then Stroke (2), Library (3), Checker (4).
echo.
pause
endlocal
