# Decentralized-Chat-Tracker-Console
Tracker (rendevous server) of decentralized chat application.\
The client side of this app is located in [this repo](https://github.com/Backgammonian/Decentralized-Chat-Peer).
## Overview:
This project implements the specialized rendevous server of [my decentralized chat application](https://github.com/Backgammonian/Decentralized-Chat-Peer). Main purpose of this console app is to help the peers to establish a direct commutication via Internet using [UDP hole punching technique](https://bford.info/pub/net/p2pnat). After connection is done peers can disconnect from the tracker if they are not planning to use it again.
## Dependencies:
* [LiteNetLib](https://github.com/RevenantX/LiteNetLib)
* [Newtonsoft.Json](https://www.newtonsoft.com/json)
* [xUnit](https://xunit.net/), [Fluent Assertions](https://fluentassertions.com/) & [FakeItEasy](https://fakeiteasy.github.io/)
