@echo off
rem Пересобирает витрину из всех моделей в design/models и открывает ее.
rem Запускать двойным кликом либо из консоли: design\tools\katalog.cmd
rem Новые модели приехали - просто запусти заново, сцена соберется с нуля.

setlocal
set BLENDER=C:\Program Files\Blender Foundation\Blender 5.2\blender.exe
set ROOT=%~dp0..\..
set MODELS=%ROOT%\design\models
set SCENE=%ROOT%\design\scene\katalog.blend
set SHOTS=%ROOT%\design\models\osmotr\katalog

if not exist "%BLENDER%" (
  echo Не найден Blender: "%BLENDER%"
  echo Поправь путь в первой строке этого файла.
  pause
  exit /b 1
)

if not exist "%ROOT%\design\scene" mkdir "%ROOT%\design\scene"
if not exist "%ROOT%\design\models\osmotr" mkdir "%ROOT%\design\models\osmotr"

echo Собираю витрину из "%MODELS%"
echo Это несколько минут: модели тяжелые.
"%BLENDER%" -b -P "%~dp0katalog.py" -- "%MODELS%" "%SHOTS%" "%SCENE%"
if errorlevel 1 (
  echo Сборка упала, сцену не открываю.
  pause
  exit /b 1
)

echo Открываю "%SCENE%"
start "" "%BLENDER%" "%SCENE%"
endlocal
