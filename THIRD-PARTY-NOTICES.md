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
| [pwr-Solaar/Solaar](https://github.com/pwr-Solaar/Solaar) | GPL-2.0 | Meaning of the Logitech HID++ 2.0 battery features. Facts only; no code was copied. |
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
