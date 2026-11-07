plugins {
    application
}

dependencies {
    implementation(project(":modules:client-runtime"))
    implementation(project(":modules:control-transport"))
    implementation(project(":modules:control-protocol"))
    implementation(project(":modules:network-core"))
    implementation(project(":modules:screen-share-client"))
    implementation(project(":modules:screen-share-protocol"))
    testImplementation("org.junit.jupiter:junit-jupiter:5.11.4")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

application {
    mainClass.set("vn.networkadmin.client.app.ClientAgentApplication")
}

java {
    sourceCompatibility = JavaVersion.VERSION_21
    targetCompatibility = JavaVersion.VERSION_21
}

tasks.withType<JavaCompile>().configureEach { options.release = 21; options.encoding = "UTF-8" }

tasks.test { useJUnitPlatform() }
