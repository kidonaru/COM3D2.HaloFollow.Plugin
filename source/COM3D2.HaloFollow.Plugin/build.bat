@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

cd /d %~dp0

set PLUGIN_NAME=COM3D2.HaloFollow.Plugin
set ASSEMBLY_NAME=COM3D2.HaloFollow
set REPO_DIR=%~dp0..\..

rem 引数: %1 = debug/release (既定 release)
set CONFIG=Release
if /i "%~1"=="debug" set CONFIG=Debug

rem .env からゲームのインストール先を読み込む ※開発者ごとの設定
set ENV_FILE=%REPO_DIR%\.env
if not exist "%ENV_FILE%" (
    echo .env が見つかりません: %ENV_FILE%
    echo .env.sample をコピーして .env を作成し、パスを設定してください
    exit /b 1
)
for /f "usebackq eol=# tokens=1,* delims==" %%a in ("%ENV_FILE%") do set "%%a=%%b"

if "%COM3D25_DIR%"=="" (
    echo .env に COM3D25_DIR が設定されていません
    exit /b 1
)

echo COM3D25_DIR: %COM3D25_DIR%

echo === ビルド中 ^(%CONFIG%^) ===
rem Release は配布物になるため常にクリーンビルドする
set BUILD_OPTIONS=
if "%CONFIG%"=="Release" set BUILD_OPTIONS=--no-incremental

dotnet build %PLUGIN_NAME%.csproj -c %CONFIG% %BUILD_OPTIONS% "/p:COM3D25_DIR=%COM3D25_DIR%"
if !ERRORLEVEL! neq 0 (
    echo ビルドに失敗しました
    exit /b 1
)

rem リリースパッケージ用に リポジトリ内 BepInEx\plugins へコピー
if not exist "%REPO_DIR%\BepInEx\plugins" mkdir "%REPO_DIR%\BepInEx\plugins"
copy /y bin\%CONFIG%\%ASSEMBLY_NAME%.dll "%REPO_DIR%\BepInEx\plugins\" >nul
if !ERRORLEVEL! neq 0 (
    echo dllのコピーに失敗しました
    exit /b 1
)

rem ゲームへのデプロイ ※ゲーム起動中は BepInEx が dll をロックするため失敗しても続行
copy /y bin\%CONFIG%\%ASSEMBLY_NAME%.dll "%COM3D25_DIR%\BepInEx\plugins\" >nul
if !ERRORLEVEL! neq 0 (
    echo 警告: ゲームへのデプロイに失敗しました ^(ゲーム起動中?^)
) else (
    echo ゲームへデプロイしました
)

exit /b 0
