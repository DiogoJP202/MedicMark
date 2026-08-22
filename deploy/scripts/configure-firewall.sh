#!/usr/bin/env bash
set -euo pipefail

# As imagens Ubuntu da Oracle terminam a cadeia INPUT com REJECT. Enquanto o
# contêiner publicava a porta diretamente, o tráfego passava pela cadeia de
# encaminhamento do Docker; o Nginx no host precisa de uma regra INPUT própria.
if ! iptables -C INPUT -p tcp -m multiport --dports 80,443 -j ACCEPT 2>/dev/null; then
    iptables -I INPUT 5 -p tcp -m multiport --dports 80,443 -j ACCEPT
fi
