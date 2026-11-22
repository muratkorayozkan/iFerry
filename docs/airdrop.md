# Native AirDrop on Windows

iFerry's AirDrop tab currently shows development status. The first target is inbound transfer from the iPhone Share → AirDrop menu to Windows using the built-in Intel AX211, PCI device ID `8086:51F0`.

| Candidate | Matching hardware | Windows driver status |
| --- | --- | --- |
| [AirDrop-for-Windows](https://github.com/21Suspect/AirDrop-for-Windows#before-you-install) | Tested on `8086:7A70` | Development driver requires Test Mode and Secure Boot off. |
| [Nekodrop](https://github.com/zheznanohana/nekodrop#supported-hardware) | Lists `8086:51F0`; discovery varies by machine | The [installer](https://github.com/zheznanohana/nekodrop/blob/50d0e9a/installer/install.ps1) locally signs the register proxy. The [WiFiCx driver notes](https://github.com/zheznanohana/nekodrop/blob/50d0e9a/docs/KERNEL.md#signing) describe a test-signed development build and point to Microsoft signing for a release. Normal-mode loading on the target PC is unverified. |

The next driver milestone is a Microsoft-signed package for the target card, followed by a local load test with Secure Boot on and Test Mode off. Driver submission requires a registered Hardware Developer Program account with an associated EV certificate. Verify the applicable production submission path before distributing the driver. [Microsoft driver signing requirements](https://learn.microsoft.com/windows-hardware/drivers/dashboard/code-signing-reqs)

## Acceptance test

1. Confirm that the signed driver loads for `8086:51F0` on Windows x64 with Secure Boot on and Test Mode off.
2. Leave Wi-Fi and Bluetooth radios enabled. Disconnect Ethernet and any shared Wi-Fi network. Use the built-in radio for peer discovery and AWDL.
3. From the iPhone Share → AirDrop menu, select iFerry. Accept a test file on Windows and verify its bytes in Documents → iFerry and its entry in the shared received-files list.
4. Stop the receiver and verify normal Windows Wi-Fi operation. Repeat on a second independently verified Windows PC before broadening hardware support.
