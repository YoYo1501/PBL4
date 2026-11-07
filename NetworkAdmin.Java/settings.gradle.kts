rootProject.name = "network-admin-java"

include(":modules:screen-share-protocol", ":modules:screen-share-client", ":modules:screen-share-server", ":modules:network-core", ":modules:control-protocol", ":modules:control-transport", ":modules:client-runtime", ":modules:admin-runtime", ":modules:admin-app", ":modules:client-app")

project(":modules:screen-share-protocol").projectDir = file("modules/screen-share-protocol")
project(":modules:screen-share-client").projectDir = file("modules/screen-share-client")
project(":modules:screen-share-server").projectDir = file("modules/screen-share-server")
project(":modules:network-core").projectDir = file("modules/network-core")
project(":modules:control-protocol").projectDir = file("modules/control-protocol")
project(":modules:control-transport").projectDir = file("modules/control-transport")
project(":modules:client-runtime").projectDir = file("modules/client-runtime")
project(":modules:admin-runtime").projectDir = file("modules/admin-runtime")
project(":modules:admin-app").projectDir = file("modules/admin-app")
project(":modules:client-app").projectDir = file("modules/client-app")
