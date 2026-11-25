# Linux test helpers

Used to check Banana Nana on a KDE Wayland laptop without touching the keyboard.

- probe.py: the first Python proof that typing a code and pasting formatted text works on Wayland.
- typer2.py / typer3.py: a fake keyboard (uinput) that types codes; typer3 takes key tokens like LEFTSHIFT+2 or RIGHTALT+A.
- editor.html: a plain page you can type formatted text into, for checking pastes.
- run-banananana.sh: starts the app next to it; uses newgrp input until the login session has the input group.

Before running a typer, make sure the test window is in front: keys go to whatever window has focus.
