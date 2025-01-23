# TestEQplatform1
Этот репозиторий содержит код небольшого драйвера ASCOM для экваториальной платформы.
Вид драйвера: соединение по COM, исполняемый файл EXE.
Версия платформы ASCOM 7.
Тип оборудования: Telescope, интерфейс v4

Установка:
Проект ReplaceWithYourName1 содержит непосредственно код драйвера. Для создания установщика драйвера необходимо скачать программу InnoSetup. Также потребуется ASCOM платформа разработчика https://ascom-standards.org/Downloads/PlatDevComponents.htm В папке установки \\ASCOM\Developer\Installer Generator\ отыщите приложение InstallerGen.exe. С его помощью легко создать скрипт для InnoSetup. На полученный таким образом инсталлятор может реагировать Windows Defender - исполняемые файлы будут автоматически удалены.
Дополнительные инструкции https://ascom-standards.org/COMDeveloper/DriverDist.htm

Проект WapProjTemplate1 нужен для публикации PowerShell инсталлятора для Windows, в этом случае можно ограничиться средствами Visual Studio и не использовать InnoSetup. Однако будет необходимо зарегестрировать установленный драйвер в платформе ASCOM. https://ascom-standards.org/COMDeveloper/DriverImpl.htm#:~:text=Registering%20(and%20Unregistering)%20for%20ASCOM

Запуск драйвера осуществляется из папки установки 

Функционал данной платформы включает:
Tracking
FindHome
PulseGuide - только по экваториальной оси.
