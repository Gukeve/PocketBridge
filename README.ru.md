# PocketBridge

[English](README.md) · [简体中文](README.zh-CN.md)

PocketBridge — независимое open-source приложение для Windows, предназначенное
для обнаружения, управления и обслуживания Android-устройств через ADB. Оно
совместимо с официальным runtime scrcpy, но **не связано с Genymobile, не
спонсируется и не одобрено Genymobile**. scrcpy и связанные товарные знаки
принадлежат их владельцам.

## Возможности

- USB и классический ADB over TCP/IP в списке устройств с обязательной
  привязкой каждой команды к serial.
- Встроенное H.264-отображение по протоколу scrcpy-server 4.1 через
  FFmpeg/libavcodec и low-latency WPF renderer.
- Несколько независимых embedded-сеансов для разных устройств.
- Отдельная резервная команда запуска официального окна scrcpy.
- Back, Home, Recents, громкость, питание, screenshot и подтверждаемая
  перезагрузка Android.
- Установка APK и файловый менеджер в пределах `/storage/emulated/0`.
- Русская, английская и упрощённая китайская локализация.
- Автоматическая подготовка runtime без ручного выбора `adb.exe` или
  `scrcpy.exe`.

PocketBridge не выбирает первый найденный телефон автоматически. Каждая
device-зависимая команда ADB/scrcpy содержит serial выбранного устройства.

## Требования

- Windows 10 или Windows 11 x64.
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- Android-устройство с разрешённой USB-отладкой либо уже авторизованная точка
  ADB TCP/IP.

## Установка runtime

Публичный ZIP PocketBridge не переупаковывает сторонние native-бинарники. При
первичной настройке приложение напрямую загружает закреплённые официальные
архивы и проверяет опубликованные SHA-256:

- scrcpy Windows x64 4.1 из GitHub Releases `Genymobile/scrcpy`;
- Android SDK Platform-Tools 37.0.0 с `dl.google.com`.

Компоненты хранятся раздельно:

```text
runtime/
├── scrcpy/
└── platform-tools/
```

В developer checkout ту же операцию выполняет:

```powershell
.\prepare-runtime.ps1
```

Старый каталог `tools/` оставлен только как compatibility fallback и не входит
в публичные source/release artifacts. Перед созданием сборки со встроенными
runtime-бинарниками обязательно изучите
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Быстрый запуск

1. Включите Developer options и USB debugging на Android.
2. Подключите устройство и подтвердите авторизацию на его экране.
3. Запустите PocketBridge, установите предлагаемый runtime и выберите нужное
   устройство.
4. Нажмите **Подключиться** для embedded-режима или **Открыть в отдельном окне**
   для официального клиента scrcpy.

Для ADB по Wi-Fi сначала авторизуйте устройство через USB, затем используйте
**Wi-Fi → Включить и подключить**. Команда `adb -s SERIAL tcpip 5555` выполняется
только для выбранного устройства.

## Сборка и проверки

Версия .NET SDK 8.0.423 закреплена в `global.json`.

```powershell
dotnet restore .\PocketBridge.sln --locked-mode
dotnet build .\PocketBridge.sln -c Release --no-restore
dotnet run --project .\PocketBridge.Tests\PocketBridge.Tests.csproj -c Release --no-build
dotnet publish .\PocketBridge.App\PocketBridge.App.csproj -c Release --self-contained false
```

GitHub Actions собирает и тестирует Windows x64, после чего сохраняет ZIP как
workflow artifact. Workflow не создаёт GitHub Release и ничего не публикует.

## Безопасность и конфиденциальность

Не размещайте в issues ADB serial, screenshots устройств, логи, runtime
manifests, IP-адреса, signing keys и локальные настройки. Порядок приватного
сообщения об уязвимости описан в [SECURITY.md](SECURITY.md).

## Лицензии

Оригинальный код PocketBridge распространяется по
[Apache License 2.0](LICENSE). Часть protocol-кода адаптирована из scrcpy под
Apache-2.0 с сохранением исходных copyright notices. Все остальные компоненты
остаются под собственными лицензиями: см.
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) и [`licenses/`](licenses/).

Правила участия: [CONTRIBUTING.md](CONTRIBUTING.md).
