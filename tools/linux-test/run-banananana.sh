#!/bin/sh
# Start Banana Nana; use newgrp only until the login session has the 'input' group.
if id -nG | tr ' ' '\n' | grep -qx input; then exec $(dirname "$(readlink -f "$0")")/banananana "$@"; fi
echo "exec $(dirname "$(readlink -f "$0")")/banananana $*" | exec newgrp input
