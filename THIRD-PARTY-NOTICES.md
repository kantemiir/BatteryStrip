# Third-party notices

BatteryStrip contains no third-party code. It uses no NuGet packages at run time; the test project uses xUnit.
Its knowledge of device protocols comes from these open-source projects and write-ups, which we thank:

| Source | License | What we learned from it |
|---|---|---|
| [deddroid/logitech-g435-battery](https://github.com/deddroid/logitech-g435-battery) | MIT | The service channel of the Logitech G435 receiver 046D:0ACB and its battery query. |
| [portyporty/g435-battery-percentage](https://github.com/portyporty/g435-battery-percentage) | MIT | The Li-Po voltage-to-percent curve in `LiPoBattery`, used as published. |
| [endes: USB HID down the rabbit hole](https://dev.to/endes/usb-hid-down-the-rabbit-hole-logitech-g435-dongle-33if) | blog post | How the G435 receiver's bridge protocol is framed. |
| [Alanus96/omarchy-ajazz-mouse](https://github.com/Alanus96/omarchy-ajazz-mouse) | MIT | The COMPX battery request and checksum. |
| [ryan-mant/AreYouLowBattery](https://github.com/ryan-mant/AreYouLowBattery) | MIT | COMPX reply layout, the asleep reply, the full-charge top-up cycle. |
| [pwr-Solaar/Solaar](https://github.com/pwr-Solaar/Solaar) | GPL-2.0 | Meaning of the Logitech HID++ 2.0 battery features and the HID++ 1.0 battery registers 0x07 and 0x0D; the "Centurion" transport of the G522 and PRO X 2 LIGHTSPEED (frame layout, device address, bridge to the headset, battery feature 0x0104). Facts only; no code was copied. |
| [HID: logitech-headset: add a battery driver for the PRO X 2](https://lwn.net/Articles/1093805/) (Linux patch, 2026) | GPL-2.0 | A battery request and answer of the PRO X 2 LIGHTSPEED as captured on the device, used as test data. Facts only; no code was copied. |
| [krisgfx9/Wireless-Device-Battery-Monitor](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor) (Wireless Battery) | GPL-3.0 | Where we found, gathered in one place, the protocols of Razer, SteelSeries, Corsair, HyperX, Glorious, Hitscan, Kysona, Pulsar and ATK devices, the ATK N9 Pro serial port, the standard HID battery usages, the HID++ 1.0 fallback, per-model voltage curves of Logitech headsets and the HeadsetControl bridge. Facts only; no code was copied. See *Credits* in the README. |
| [openrazer/openrazer](https://github.com/openrazer/openrazer) | GPL-2.0 | Razer report layout, transaction ids, battery and charging commands. Facts only; no code was copied. |
| [Sapd/HeadsetControl](https://github.com/Sapd/HeadsetControl) | GPL-3.0 | SteelSeries, Corsair and HyperX headset battery commands, Logitech headset voltage calibration, and the `-o json` output the optional bridge reads. Facts only; HeadsetControl is not bundled, the app runs it only if you installed it. |
| [flozz/rivalcfg](https://github.com/flozz/rivalcfg) | WTFPL | Battery commands of SteelSeries Aerox, Prime and Rival mice; the Rival Pro from its pull request 294. |
| [ikajdan/glorious-indicator](https://github.com/ikajdan/glorious-indicator) | GPL-3.0 | The Glorious battery feature report. Facts only; no code was copied. |
| [louis4craft/glorious-ctl](https://github.com/louis4craft/glorious-ctl) | MIT | The Glorious battery feature report and its status byte. |
| [Medprj/HyperXWM](https://github.com/Medprj/HyperXWM) | none stated | HyperX Cloud III Wireless battery commands. Facts only; no code was copied. |
| Linux `hid-kysona` driver (Lode Willems) | GPL-2.0 | Kysona, Pulsar and ATK battery and online-status commands and their checksum. Facts only; no code was copied. |
| Hitscan and ATK web configurators | — | The Hitscan battery request and the ATK N9 Pro serial frames, as recorded by Wireless Battery. |
| USB HID Usage Tables (USB-IF) | specification | The standard battery usages: Battery Strength (page 0x06, usage 0x20) and Absolute State Of Charge (page 0x85, usage 0x65). |
| Linux `hid-playstation` driver, [psdevwiki](https://www.psdevwiki.com) | GPL-2.0 / wiki | Offsets of the battery fields in DualSense and DualShock 4 reports. Facts only; no code was copied. |

The voltage curve, reproduced in `src/BatteryStrip/Devices/LiPoBattery.cs`, is covered by this notice:

```
Copyright (c) 2026 Oğuz Kayra Bozkurt

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```
