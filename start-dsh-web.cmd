@echo off
cd /d "%~dp0"
title DeepSeek Harness (web :8080)
echo ==============================================
echo   DeepSeek Harness quick launcher
echo   URL: http://127.0.0.1:8080
echo   Close this window to stop the server.
echo ==============================================
echo.
rem Open the app window as soon as the server is listening on 8080.
start "" powershell -NoProfile -WindowStyle Hidden -Command "while ($true) { try { $c = New-Object Net.Sockets.TcpClient; $c.Connect('127.0.0.1', 8080); $c.Close(); break } catch { Start-Sleep -Milliseconds 500 } }; Start-Process 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' -ArgumentList '--app=http://127.0.0.1:8080'"
dsh --profile web --port 8080
if errorlevel 1 (
  echo.
  echo [!] dsh exited with an error.
  echo     If port 8080 is already in use, close the previous instance and try again.
)
echo.
echo DeepSeek Harness stopped.
pause
