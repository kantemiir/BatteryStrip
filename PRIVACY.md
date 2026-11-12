# Privacy

BatteryStrip has no telemetry, no analytics, no crash reporting and no ads.

**What stays on your computer** (`%LOCALAPPDATA%\BatteryStrip`): settings, the last known charge of each device,
a log of up to two 512 KB files, and the `--diagnose` report when you run it. Uninstalling removes all of it.

**Network**: the app goes online only if you turn on *Check for updates*. Then, once a day, it asks
GitHub for this project's feed of releases (`github.com/<owner>/BatteryStrip/releases.atom`), and when you choose to install an update it downloads the new
file from GitHub and checks its SHA-256. These requests carry the app's version in the User-Agent header and nothing
about your computer or devices. GitHub sees your IP address, as with any visit to github.com.

**Devices**: the app reads battery state locally. It sends devices only read requests (see the README for which
devices are asked and how). The Logitech G435 is read only after you agree, because reading pauses its audio.

**The `--diagnose` report** lists the HID devices of the computer (vendor and product codes, product names) and the
charges read. Bluetooth device names can include a person's name, for example "Anna's AirPods"; look through the
report before attaching it to a public issue.

---

# Приватность

Телеметрии, аналитики, отчётов о сбоях и рекламы нет.

**На компьютере** (`%LOCALAPPDATA%\BatteryStrip`) хранятся настройки, последние известные значения заряда, журнал
(не больше двух файлов по 512 КБ) и отчёт `--diagnose`, если вы его запускали. При удалении программы всё это удаляется.

**Интернет** — только если включить «Проверять обновления». Тогда раз в день программа запрашивает у GitHub
ленту версий этого проекта (`releases.atom`), а при установке обновления скачивает файл с GitHub и проверяет его контрольную сумму.
В запросах есть только версия программы; ничего о компьютере и устройствах не отправляется. GitHub видит ваш IP-адрес,
как при любом заходе на github.com.

**Устройства**: заряд читается на компьютере. Устройствам отправляются только запросы на чтение. Logitech G435 читается
только после вашего согласия, потому что при чтении прерывается звук.

**Отчёт `--diagnose`** содержит коды и названия HID-устройств и прочитанный заряд. В названиях Bluetooth-устройств
бывает имя владельца, например «AirPods Анны» — посмотрите отчёт, прежде чем прикладывать его к открытому обращению.
