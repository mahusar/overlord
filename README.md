# StealthHub - Overlord

A Tor-native chain service for Stealth (XST). An operator runs a hub beside a
Stealth daemon and it answers read-only chain queries over a hidden service. A
client asks several hubs at once and compares what they say, so it can browse the
chain without running a daemon of its own.

**StealthHub** is the desktop application. **A hub** is the service an operator
runs. **Overlord** is the project both are built in.

| Build | Runs where | Does |
|---|---|---|
| `overlordhub` | Linux, headless, beside a daemon | answers read-only chain queries; can be announced on chain by a separate command |
| StealthHub | desktop, Tor bundled | finds hubs, holds several at once, explorer, stats and the board |

One codebase. The hub half is `Assets/Overlord/Core`, which touches no Unity API
and builds as `netstandard2.0`, so the headless host is a plain console project
over the same files.

There is no wallet here and there never will be. The hub holds no keys, signs
nothing and spends nothing.

## Several hubs, not one

A client with no daemon cannot verify anything, so it asks more than one hub and
looks for disagreement. Answers are compared with volatile fields stripped, so
two honest hubs one block apart are not reported as a conflict.

## Discovery

You cannot search for a `.onion`. So the chain is the nodelist: an operator can
publish one 40-byte `OP_RETURN` carrying the hub's public key, and the address is
rebuilt from that key, so a listing can only ever point at the genuine service
for it. Listing is optional and costs a transaction. Serving does not touch the
wallet, and nothing goes on chain until `publish --yes`.

## Installing a hub

Linux, beside a synced Stealth daemon with the explore API on. Building needs
`xst-dotnet` checked out next to this repo.

**1. Build it** (on any machine with the .NET 8 SDK):

    cd src/overlordhub
    dotnet publish -c Release -r linux-x64 --self-contained false \
        -p:PublishSingleFile=true -p:DebugType=none -o ../../build/linux

**2. Copy it to the server:**

    scp build/linux/overlordhub user@server:~

**3. The .NET 8 runtime**, once, on the server:

    sudo apt update && sudo apt install -y dotnet-runtime-8.0
    dotnet --list-runtimes        # wants Microsoft.NETCore.App 8.0.x

On releases without .NET 8 in the archive, use Microsoft's script instead:

    curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
    chmod +x dotnet-install.sh
    sudo ./dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir /opt/dotnet
    sudo ln -sf /opt/dotnet/dotnet /usr/local/bin/dotnet

**4. Point it at the daemon** and check before anything else. The credentials are
the `rpcuser` and `rpcpassword` from `StealthCoin.conf`:

    chmod +x overlordhub
    export XST_RPC_USER=... XST_RPC_PASSWORD=...
    ./overlordhub check

It should print the daemon version, `allowlist passes` and `fit to serve`.

**5. Serve**, on loopback:

    ./overlordhub serve

**6. The hidden service.** Append to `/etc/tor/torrc`:

    HiddenServiceDir /var/lib/tor/overlord/
    HiddenServiceVersion 3
    HiddenServicePort 7790 127.0.0.1:7790

then restart Tor and read the address:

    sudo systemctl restart tor@default
    sudo cat /var/lib/tor/overlord/hostname

`reload` is not enough for a new hidden service, and Tor refuses to start if the
same `HiddenServiceDir` appears twice. Let Tor create that directory itself: one
made by hand has the wrong owner. Open no ports — the hub listens on loopback and
Tor reaches it locally.

That address is the hub. Paste it into the client to connect.

**7. Announce it on the chain.** Optional, costs a transaction, and permanent:

    ./overlordhub publish --onion <your>.onion --port 7790

Without `--yes` that is a dry run. Serving never touches the wallet; only this
does. `src/overlordhub/deploy/` has a systemd unit and an env file if you would
rather not keep it in a terminal.

## Requirements

Unity 6000.0.71f1 and `com.mahusar.xst-unity` for the client, `xst-dotnet`
checked out beside this repo, StealthCoind v3.3.5.0 with the explore API on,
.NET 8 for the host, Tor bundled with the client and system Tor for a hub.

## Status

Verified end to end: a client read real chain data from a hub through a real
hidden service, with `sendtoaddress` still refused. Not yet true:

- no listing exists on chain, so a client still has to be handed an address
- hubs do not gossip yet, so the peer set stays empty
- corroboration has only been tested against one daemon, not two
- the systemd unit has not been exercised on a real server

---

Experimental software, published for testing and development. No guarantees are
made about stability, security or reliability. It contains no wallet operations
of any kind. The author operates no public hub, and any hub claiming to be the
author's is not.

Copyright (C) 2026 Martin Husar. No license is granted: the source is published
for review and verification only, not for reuse, modification or redistribution.
Third-party components remain under their own licenses.
