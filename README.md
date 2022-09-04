# Decentralized-Chat-Tracker-Console
Tracker (rendevous server) of decentralized chat application. 
## Overview
This project implements rendevous server for [peers of decentralized chat application](https://github.com/Backgammonian/Decentralized-Chat-Peer). Main purpose of this console app is to help peers to establish direct commutication via Internet using [UDP hole punching technique](https://bford.info/pub/net/p2pnat). After connection is done peers can disconnect from the tracker if they are not planning to use it in future.
## Libraries used in this project:
* [LiteNetLib](https://github.com/RevenantX/LiteNetLib)
* [Newtonsoft.Json](https://www.newtonsoft.com/json)
* [Crc32.NET](https://github.com/force-net/Crc32.NET)
* [System.Security.Cryptography.Cng](https://www.nuget.org/packages/System.Security.Cryptography.Cng/)
