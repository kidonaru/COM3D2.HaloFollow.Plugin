@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

cd /d %~dp0

rem :build の直前に日本語の rem を置くと、cmd が call :build のラベル探索で行を読み違えて実行するため、そこは ASCII にする

set PLUGIN_NAME=COM3D2.HaloFollow.Plugin
set ASSEMBLY_NAME=COM3D2.HaloFollow.Plugin
set REPO_DIR=%~dp0..\..

rem 引数: %1 = debug/release (既定 release), %2 = com3d2/com3d25/all (既定 all)
set CONFIG=Release
if /i "%~1"=="debug" set CONFIG=Debug

set TARGET=all
if /i "%~2"=="com3d2" set TARGET=com3d2
if /i "%~2"=="com3d25" set TARGET=com3d25

rem .env からゲームのインストール先を読み込む ※開発者ごとの設定
set ENV_FILE=%REPO_DIR%\.env
if not exist "%ENV_FILE%" (
    echo .env が見つかりません: %ENV_FILE%
    echo .env.sample をコピーして .env を作成し、パスを設定してください
    exit /b 1
)
for /f "usebackq eol=# tokens=1,* delims==" %%a in ("%ENV_FILE%") do set "%%a=%%b"

rem COM3D2.5 版も COM3D2 側の Sybaris\lib\UnityInjector.dll を参照するため、COM3D2_DIR は常に要る
if "%COM3D2_DIR%"=="" (
    echo .env に COM3D2_DIR が設定されていません
    exit /b 1
)
echo COM3D2_DIR: %COM3D2_DIR%
if not "%TARGET%"=="com3d2" (
    if "%COM3D25_DIR%"=="" (
        echo .env に COM3D25_DIR が設定されていません
        exit /b 1
    )
    echo COM3D25_DIR: %COM3D25_DIR%
)

rem Release は配布物になるため常にクリーンビルドする
set BUILD_OPTIONS=
if "%CONFIG%"=="Release" set BUILD_OPTIONS=--no-incremental

rem ============ COM3D2 版 ============
if not "%TARGET%"=="com3d25" (
    call :build net35 "UnityInjector" "%COM3D2_DIR%" COM3D2
    if !ERRORLEVEL! neq 0 exit /b 1
)

rem ============ COM3D2.5 版 ============
if not "%TARGET%"=="com3d2" (
    call :build net471 "UnityInjector25" "%COM3D25_DIR%" COM3D2.5
    if !ERRORLEVEL! neq 0 exit /b 1
)

exit /b 0

rem :build TargetFramework RepoOutDir GameDir DisplayName
:build
echo === %~4 版をビルド中 ^(%CONFIG%^) ===
dotnet build %PLUGIN_NAME%.csproj -c %CONFIG% -f %~1 %BUILD_OPTIONS% "/p:COM3D2_DIR=%COM3D2_DIR%" "/p:COM3D25_DIR=%COM3D25_DIR%"
if !ERRORLEVEL! neq 0 (
    echo %~4 版のビルドに失敗しました
    exit /b 1
)

rem リリースパッケージ用に リポジトリ内へコピーする
if not exist "%REPO_DIR%\%~2" mkdir "%REPO_DIR%\%~2"
copy /y bin\%CONFIG%\%~1\%ASSEMBLY_NAME%.dll "%REPO_DIR%\%~2\" >nul
if !ERRORLEVEL! neq 0 (
    echo dllのコピーに失敗しました
    exit /b 1
)

rem ゲームへのデプロイ ※ゲーム起動中は dll がロックされるため失敗しても続行
copy /y bin\%CONFIG%\%~1\%ASSEMBLY_NAME%.dll "%~3\Sybaris\UnityInjector\" >nul
if !ERRORLEVEL! neq 0 (
    echo 警告: %~4 へのデプロイに失敗しました ^(ゲーム起動中?^)
) else (
    echo %~4 へデプロイしました
)
exit /b 0
