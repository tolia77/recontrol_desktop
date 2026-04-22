@echo off
set API_BASE_URL=http://192.168.122.1:3003
set WS_URL=ws://192.168.122.1:3003/cable
dotnet "Z:\recontrol_desktop\publish\ReControl.Desktop.dll"
echo.
echo Exit code: %ERRORLEVEL%
pause
