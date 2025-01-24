# TestEQplatform1
This repository contains code for a small ASCOM driver for the equatorial platform for astronomical telescope on dobsonian mount.
Driver type: COM connection, EXE executable file.
ASCOM platform version 7.
Equipment type: Telescope, v4 interface.
The equatorial platform is driven by a NEMA17 stepper motor with a 1:99 planetary gearbox. The motor is controlled via an Arduino Nano board.

Installation:
The ReplaceWithYourName1 project contains the driver code itself. To create a driver installer, you need to download the InnoSetup program. You will also need the ASCOM developer platform https://ascom-standards.org/Downloads/PlatDevComponents.htm In the installation folder \\ASCOM\Developer\Installer Generator\, find the InstallerGen.exe application. It makes it easy to create a script for InnoSetup. Windows Defender can respond to the installer obtained in this way - the executable files will be automatically deleted.

Additional installation instructions https://ascom-standards.org/COMDeveloper/DriverDist.htm


The WapProjTemplate1 project is needed to publish a PowerShell installer for Windows; in this case, you can limit yourself to Visual Studio tools and not use InnoSetup. However, it will be necessary to register the installed driver with the ASCOM platform. https://ascom-standards.org/COMDeveloper/DriverImpl.htm#:~:text=Registering%20(and%20Unregistering)%20for%20ASCOM


The driver is launched from the installation folder


The functionality of this platform includes:
Tracking
FindHome
PulseGuide - equatorial axis only.
