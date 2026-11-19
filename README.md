# ITE EC Reader v2.0

Uses PawnIO to read and write to the EC chip. 

Install PawnIO here: https://pawnio.eu/
.NET 8 is required for the app to run: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.23-windows-x64-installer

<img width="1439" height="899" alt="image" src="https://github.com/user-attachments/assets/55adb752-b070-4662-8ac2-3e663972f45e" />

v2.0 is a WPF desktop app (v1 was a console app).

## Features

- **Hex grid** of the current 256-byte page with HEX/DEC toggle and an ASCII strip.
  Changed bytes flash red, `0xFF` is green, `0x00` is dimmed.
- **Sidebar** with every EC RAM page (labelled: PWM, GPIO, SMBus, SRAM-x…) or every
  Super I/O logical device in the **LPC devices** view. Filter by name or type an address like `1600`.
- **Direct editing**: left-click a byte, type hex digits (only 0-9 / A-F are accepted), press enter to write to the register.
- **Bit editor**: right-click a byte to see and toggle its 8 bits before writing.
- **Fans & PWM**: live TACH RPM, CTR values and a slider per DCR (PWM duty) register.
- **Record / playback**: press Record to start right away; press Stop and a *Save as* window asks
  where to save it (`.ecrec`). Then scrub through frames, play them back and step pages
  (only recorded pages are listed). 
- **Recording settings** (button next to Record): pick which of the 256 EC pages
  (0x0000 to 0xFF00) to record. Search by name or address.
- **Settings**: polling interval (100 / 300 / 500 / 1000 ms), EC ports, base address,
  and how long changes stay highlighted.

Writes are **locked by default**. Turn on *Allow writes* in the header, or confirm the prompt
the first time you press Enter on an edited byte.

## Keyboard

| Key | Action |
| --- | --- |
| Click + `0-9` `A-F`, `Enter` | Type a value into the selected byte and write it |
| `Esc` | Cancel edit → clear selection → close recording |
| Arrows | Move the selected byte (with nothing selected: Left/Right = 1 page, Up/Down = 5 pages, like v1) |
| `Ctrl` + Arrows | Change page |
| `PageUp` / `PageDown` | Previous / next page |
| `Ctrl+R` | Start / stop recording |
| `Ctrl+O` | Open a recording |
| `,` / `.` | Previous / next frame while viewing a recording |
| `Ctrl+P` | Pause / resume polling |
| `Ctrl+L` / `Ctrl+M` | LPC devices / EC RAM view |
| `Ctrl+G` | Go to address |

# v1.0 (deprecated):
<img width="782" height="388" alt="Screenshot 2026-11-16 134319" src="https://github.com/user-attachments/assets/32a94bca-56d9-4c42-bd64-a8df61373556" />

# DISCLAIMER: USE AT YOUR OWN RISK. IM NOT RESPONSIBLE FOR ANY ISSUES CAUSED BY THIS SOFTWARE.
