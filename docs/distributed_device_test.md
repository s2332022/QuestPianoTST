# Distributed device test

1. Put PC and Quest on the same private Wi-Fi. Allow the Windows player on Private networks, or add inbound UDP 50000 and 50002 and outbound UDP 50001. Do not expose these unauthenticated v1 ports to public networks.
2. Find the PC IPv4 with `ipconfig`; enter it in Quest. Ports are editable in `DistributedSettings` and must match.
3. Open/build `PianoDistributedHost`, Start Network, refresh/select/connect MIDI. Open/build `PianoDistributedQuest`, set PC IP, Connect, and complete A/B/C calibration if needed.
4. Wait for Connected, at least 10 clock samples, and stable RTT. Start Session on PC.
5. Run 30 seconds: 0-5 hands still; 5-10 C4 five times; 10-15 C4 D4 E4 F4 G4; 15-20 C4/E4/G4 chord; 20-25 play using CC64; 25-30 move hands out of view and return. Stop Session on PC.
6. Confirm both hands/joints and head rows, MIDI/CC64, keyboard chord state, tracking loss/recovery, >=10 clock rows over a sufficiently long connection, nonnegative sequence loss, eight closed files, and successful reopening/renaming of every file.

