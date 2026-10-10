# Notice

## Credits

The **firmware flasher** parts of the Fit3 Manager (`pc/Fwd.cs`, `pc/Watch.cs` and the transfer code in `android-manager/src/com/fit3/manager/Fwd.java` and `Flash.java`) implement the same package checks and the same Bluetooth transfer protocol as **[fit3-flasher](https://github.com/yuriyurin/fit3-flasher) by yuriyurin**, and were written by studying how that project works. All credit for the original idea and the reverse engineering of the protocol goes to its author.

That project did not carry a license when this port was made. It is published here at the repository owner's decision. If you are the author and want the port changed, credited differently or removed, please open an issue and it will be handled promptly.

## What is original here

The light-app system (SDK, loader, `.f3a` format, the 22 apps), the Fit3 Apps installer, the app creator and the Manager's user interface are original work.

## No firmware

No Samsung firmware or firmware image is included, and none should be committed (`*.bin` is ignored). Galaxy Fit3 and Samsung are trademarks of their owners; this project is unofficial and not affiliated with them.

## Use at your own risk

Flashing firmware can make a watch unusable, and light apps run as native code on the watch with no sandbox. Everything here has been tested on a PC only; nothing has been verified on a real watch yet.
